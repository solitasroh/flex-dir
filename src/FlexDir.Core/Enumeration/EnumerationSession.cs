using System.Runtime.CompilerServices;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Core.Enumeration;

/// <summary>
/// 한 페인의 열거를 관리한다. 새 열거를 시작하면 이전 것을 취소한다.
/// <para>
/// 폴더를 빠르게 넘기는 것은 정상 조작이고, 이전 폴더의 항목이 새 폴더 목록에 섞이는 것은
/// 전작의 잔버그 원천이었다 (docs/PRD.md §4 열거 중 폴더 이탈). 격리 수단은 세대 번호다 —
/// <see cref="EnumerationRun"/> 은 배치를 낼 때마다 자기 세대가 아직 현재 세대인지 확인한다.
/// </para>
/// <para>
/// 열거를 <c>lock</c> 으로 감싸지 않는다. 열거는 네트워크 경로에서 초 단위로 걸리고 그 사이
/// <see cref="Start"/> 가 막히면 폴더 전환 자체가 멈춘다. 세대 번호는
/// <see cref="Interlocked"/> 로 다룬다.
/// </para>
/// </summary>
public sealed class EnumerationSession : IAsyncDisposable
{
    private readonly IFolderSource _source;

    private int _generation;
    private EnumerationRun? _current;

    public EnumerationSession(IFolderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
    }

    /// <summary>현재 세대 번호. <see cref="Start"/> 마다 증가한다.</summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>이전 run 을 취소하고 새 run 을 시작한다.</summary>
    public EnumerationRun Start(LocationId folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // 세대를 먼저 올린다. 취소가 먼저 보이면 이전 run 이 '아직 낡지 않은' 상태에서
        // 실패를 만나 그 오류를 소비자에게 올릴 수 있다 — 낡은 실패는 삼켜야 한다.
        var generation = Interlocked.Increment(ref _generation);
        var run = new EnumerationRun(this, _source, folder, generation);
        var previous = Interlocked.Exchange(ref _current, run);

        // 반환 전에 취소한다. 호출자가 새 목록을 채우기 시작할 때 이전 열거는 이미 접혀 있다.
        previous?.Cancel();

        return run;
    }

    /// <summary>진행 중인 run 을 취소하고 완료를 기다린다.</summary>
    public async ValueTask DisposeAsync()
    {
        var run = Volatile.Read(ref _current);
        if (run is null)
        {
            return;
        }

        run.Cancel();
        await run.WaitUntilFinishedAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="EnumerationSession.Start"/> 한 번에 대응하는 열거. 항목을 배치로 낸다.
/// 한 번만 소비한다 — 한 페인의 한 폴더에 대응하는 일회성 흐름이다.
/// </summary>
public sealed class EnumerationRun
{
    /// <summary>첫 배치는 항목 1개다 — 첫 항목을 최대한 빨리 보인다 (docs/PRD.md §5, 150ms 목표).</summary>
    private const int FirstBatchSize = 1;

    /// <summary>
    /// 이후 배치 크기. 시간이 아니라 항목 수로만 끊는다 — 시간 기반 플러시를 넣으면
    /// 배치 경계가 실행 속도에 따라 달라지고 테스트가 간헐적으로 실패한다.
    /// </summary>
    private const int BatchSize = 256;

    private const int NotConsumed = 0;
    private const int Consuming = 1;
    private const int Finished = 2;

    private readonly EnumerationSession _session;
    private readonly IFolderSource _source;

    // Dispose 하지 않는다. run 의 완료와 Start 의 취소가 경합하면 이미 Dispose 된 원본에
    // Cancel 이 들어와 ObjectDisposedException 이 되고, 그 예외는 폴더 전환 경로에서 터진다.
    // 타이머를 걸지 않은 CancellationTokenSource 는 비관리 자원을 잡지 않는다.
    private readonly CancellationTokenSource _cts = new();

    // RunContinuationsAsynchronously: 완료를 알리는 스레드가 곧 열거를 미는 소비자 스레드다.
    // 대기자의 이어붙은 코드를 그 스레드에서 그대로 돌리면 미는 손이 멈춘다.
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _state;

    internal EnumerationRun(EnumerationSession session, IFolderSource source, LocationId folder, int generation)
    {
        _session = session;
        _source = source;
        Folder = folder;
        Generation = generation;
    }

    public LocationId Folder { get; }

    /// <summary>이 run 이 시작될 때의 세대 번호.</summary>
    public int Generation { get; }

    /// <summary>이 run 이 이미 낡았는가 (더 새로운 <see cref="EnumerationSession.Start"/> 가 있었는가).</summary>
    public bool IsStale => Generation != _session.Generation;

    /// <summary>
    /// 항목을 배치로 낸다. 낡은 run 에서는 아무것도 내지 않는다.
    /// <para>
    /// 낡아진 run 과 세션이 접은 run 은 <b>예외 없이</b> 조용히 끝난다
    /// (<see cref="OperationCanceledException"/> 도 아니다) — 폴더를 빠르게 넘기는 것은 정상
    /// 조작이고, 소비자에게 예외를 주면 UI 가 오류 상태로 넘어간다. 소비자가
    /// <paramref name="ct"/> 로 직접 취소한 경우만 그 예외가 나간다.
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<FileItem>> BatchesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 세션이 이미 이 run 을 접었다면(Dispose) 소비를 시작하지 않는다.
        if (Interlocked.CompareExchange(ref _state, Consuming, NotConsumed) != NotConsumed)
        {
            yield break;
        }

        try
        {
            // 낡은 run 은 열거를 시작조차 하지 않는다. 이미 떠난 폴더에 I/O 를 걸 이유가 없다.
            if (IsStale)
            {
                yield break;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            await using var items = _source
                .EnumerateAsync(Folder, linked.Token)
                .GetAsyncEnumerator(linked.Token);

            var batch = new List<FileItem>();
            var target = FirstBatchSize;

            // 조용히 접힌 run 인가. 받아둔 항목을 마지막 배치로 낼지 여기서 갈린다.
            var quiet = false;

            while (true)
            {
                var moved = false;

                try
                {
                    moved = await items.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 세션이 이 run 을 접었다. 소비자가 건 취소가 아니므로 오류가 아니다.
                    quiet = true;
                }
                catch (LocationAccessException) when (IsStale)
                {
                    // 이미 떠난 폴더의 실패다. 새 폴더의 오류로 표시하면 안 된다.
                    quiet = true;
                }

                if (!moved)
                {
                    break;
                }

                batch.Add(items.Current);

                if (batch.Count < target)
                {
                    continue;
                }

                if (IsStale)
                {
                    // 받아둔 항목은 버린다. 진실원천은 파일시스템이고(CLAUDE.md §4),
                    // 섞인 목록은 되돌릴 수 없다.
                    yield break;
                }

                yield return batch;

                batch = [];
                target = BatchSize;
            }

            // 스트림이 끝났다. 남은 항목이 마지막 배치다 (0개면 배치를 내지 않는다).
            if (!quiet && batch.Count > 0 && !IsStale)
            {
                yield return batch;
            }
        }
        finally
        {
            Finish();
        }
    }

    internal void Cancel() => _cts.Cancel();

    internal Task WaitUntilFinishedAsync()
    {
        // 소비가 시작되지 않았으면 기다릴 것이 없다. 여기서 끝난 것으로 표시해
        // 뒤늦게 시작하는 소비가 아무것도 내지 않게 한다.
        if (Interlocked.CompareExchange(ref _state, Finished, NotConsumed) == NotConsumed)
        {
            _finished.TrySetResult();
        }

        return _finished.Task;
    }

    private void Finish()
    {
        Interlocked.Exchange(ref _state, Finished);
        _finished.TrySetResult();
    }
}
