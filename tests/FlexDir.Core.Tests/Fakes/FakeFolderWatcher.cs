using System.Runtime.CompilerServices;
using System.Threading.Channels;

using FlexDir.Core.Locations;
using FlexDir.Core.Watching;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IFolderWatcher"/> 의 채널 기반 fake. 파일시스템을 감시하지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면 DI 조립에서
/// 실수로 주입될 수 있다. 실제 구현체(<c>FileSystemWatcher</c>)는 <c>FlexDir.Shell</c> 의
/// 몫이고 수동 검증 대상이다 (CLAUDE.md §1·§5).
/// </para>
/// <para>
/// 감시는 외부에서 일어나는 일이라 테스트가 시점을 잡을 수 없다. 그래서 이 fake 는
/// <see cref="Push"/>·<see cref="PushOverflow"/>·<see cref="Complete"/> 로 <b>테스트가
/// 시점을 정한다</b> — 갱신 중 선택 유지(ADR-011) 같은 성질을 시간에 기대지 않고 검증하려면
/// 이것이 필요하다. phase 2 의 감시 통합 테스트가 이 fake 를 주입해 쓴다.
/// </para>
/// <para>
/// <b>세대마다 채널을 따로 연다</b> (<see cref="WatchStream"/>). 인스턴스 하나가 채널을
/// 공유하면 낡은 세대에 밀어넣은 변경을 새 세대가 읽어 가고, 그러면 "낡은 감시가 밀어넣었다"
/// 는 테스트가 실제로는 새 감시를 재게 된다 — 감시 세대 격리가 통째로 빠져도 통과하는
/// 테스트가 그렇게 만들어졌다.
/// </para>
/// </summary>
public sealed class FakeFolderWatcher : IFolderWatcher
{
    private readonly Lock gate = new();

    /// <summary>
    /// 아직 감시가 걸리지 않았을 때 밀어넣은 변경. 첫 감시가 이 채널을 그대로 받는다 —
    /// "감시를 걸기 전에 일어난 변경" 을 테스트가 미리 준비할 수 있어야 한다.
    /// </summary>
    private Channel<FolderChange>? staged;

    private WatchStream? current;
    private bool completed;

    /// <summary><see cref="WatchAsync"/> 가 호출된 폴더의 순서.</summary>
    public List<LocationId> WatchCalls { get; } = [];

    /// <summary>취소가 관측된 횟수. 감시 스트림 하나가 취소로 끝날 때마다 1 늘어난다.</summary>
    public int CancellationsObserved { get; private set; }

    /// <summary>
    /// 가장 최근에 걸린 감시 스트림. 폴더를 옮기면 새 것으로 바뀌므로, 참조를 미리 잡아두면
    /// <b>낡아진 세대에만</b> 변경을 밀어넣을 수 있다.
    /// </summary>
    public WatchStream? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <summary>지금 감시 중인 소비자에게 변경을 밀어넣는다.</summary>
    public void Push(FolderChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        Channel<FolderChange> target;

        lock (gate)
        {
            target = current?.Changes ?? (staged ??= NewChannel());
        }

        Write(target, change);
    }

    /// <summary>감시 버퍼 오버플로를 밀어넣는다.</summary>
    public void PushOverflow() => Push(FolderChange.Overflowed);

    /// <summary>스트림을 정상 종료시킨다. 감시 대상이 사라진 경우에 해당한다.</summary>
    public void Complete()
    {
        lock (gate)
        {
            // 이후에 걸리는 감시도 즉시 끝나야 한다. 그러지 않으면 Complete 뒤에 감시를 거는
            // 테스트가 실패하지 않고 매달린다.
            completed = true;
            staged?.Writer.TryComplete();
            current?.Changes.Writer.TryComplete();
        }
    }

    public IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        lock (gate)
        {
            // 호출 시점에 기록한다. 반복자 본문은 첫 MoveNextAsync 까지 실행되지 않으므로
            // 안에서 기록하면 "호출했는가" 가 아니라 "소비를 시작했는가" 를 재는 것이 된다.
            WatchCalls.Add(folder);

            var channel = staged ?? NewChannel();
            staged = null;

            current = new WatchStream(this, folder, channel, ct);

            return current;
        }
    }

    private Channel<FolderChange> NewChannel()
    {
        // 무한 채널이다. 소비자가 없을 때 밀어넣은 변경은 버퍼에 남아 그 세대의 소비자가 받는다.
        var channel = Channel.CreateUnbounded<FolderChange>();

        if (completed)
        {
            channel.Writer.TryComplete();
        }

        return channel;
    }

    private void ObserveCancellation()
    {
        lock (gate)
        {
            CancellationsObserved++;
        }
    }

    private static void Write(Channel<FolderChange> channel, FolderChange change)
    {
        // 조용히 삼키면 테스트가 "변경을 밀어넣었다" 고 믿은 채 매달린다.
        if (!channel.Writer.TryWrite(change))
        {
            throw new InvalidOperationException("Complete 이후에는 변경을 밀어넣을 수 없다.");
        }
    }

    /// <summary>
    /// <see cref="WatchAsync"/> 한 번에 대응하는 감시 스트림. 한 세대다.
    /// </summary>
    public sealed class WatchStream : IAsyncEnumerable<FolderChange>
    {
        // RunContinuationsAsynchronously: 이 신호를 켜는 스레드는 감시 루프의 스레드다.
        // 기다리는 테스트 코드를 그 스레드에서 그대로 돌리면 루프가 끝나는 손이 멈춘다.
        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly FakeFolderWatcher owner;
        private readonly CancellationToken ct;

        internal WatchStream(
            FakeFolderWatcher owner,
            LocationId folder,
            Channel<FolderChange> changes,
            CancellationToken ct)
        {
            this.owner = owner;
            this.ct = ct;
            Folder = folder;
            Changes = changes;
        }

        /// <summary>이 세대가 보는 폴더.</summary>
        public LocationId Folder { get; }

        /// <summary>
        /// 소비자가 이 스트림을 놓았을 때 완료된다.
        /// <para>
        /// 낡은 세대의 처리가 끝났음을 기다릴 수 있는 유일한 지점이다 — 취소는 반복자를 바로
        /// 끝내지만 그 시점에 소비자는 아직 <b>받아둔 변경을 적용하는 중</b>일 수 있다.
        /// 그래서 취소 관측(<see cref="CancellationsObserved"/>)으로 기다리면 아직 진행 중인
        /// 것을 끝났다고 보게 된다.
        /// </para>
        /// </summary>
        public Task Finished => finished.Task;

        internal Channel<FolderChange> Changes { get; }

        /// <summary>이 세대에만 변경을 밀어넣는다. 다른 세대는 읽을 수 없다.</summary>
        public void Push(FolderChange change)
        {
            ArgumentNullException.ThrowIfNull(change);

            Write(Changes, change);
        }

        // 반복자에 넘긴 ct 와 여기서 받은 것을 컴파일러가 함께 묶는다 —
        // 감시를 걸 때 준 토큰과 소비를 시작할 때 준 토큰 어느 쪽으로도 끝나야 한다.
        public IAsyncEnumerator<FolderChange> GetAsyncEnumerator(CancellationToken token = default)
            => new Enumerator(Read(ct).GetAsyncEnumerator(token), finished);

        private async IAsyncEnumerable<FolderChange> Read(
            [EnumeratorCancellation] CancellationToken token)
        {
            while (true)
            {
                FolderChange change;

                try
                {
                    // 토큰을 넘겨야 한다. CancellationToken.None 을 넘기면 변경이 오지 않는 동안
                    // ReadAsync 가 풀리지 않고, 그러면 이 반복자를 dispose 하는 쪽도 영원히
                    // 대기한다 — 테스트가 실패하지 않고 dotnet test 자체가 매달린다.
                    change = await Changes.Reader.ReadAsync(token);
                }
                catch (OperationCanceledException)
                {
                    // 취소는 정상 종료다 (계약). 예외로 내면 폴더 전환마다 예외 경로를 탄다.
                    owner.ObserveCancellation();
                    yield break;
                }
                catch (ChannelClosedException)
                {
                    // Complete() — 낼 것이 더 없다.
                    yield break;
                }

                yield return change;
            }
        }

        /// <summary>
        /// 소비자가 스트림을 놓는 시점을 <see cref="Finished"/> 로 알리는 껍데기.
        /// 반복자 자신은 취소 시점에 이미 끝나 있으므로 그 안에서는 알릴 수 없다.
        /// </summary>
        private sealed class Enumerator(IAsyncEnumerator<FolderChange> inner, TaskCompletionSource finished)
            : IAsyncEnumerator<FolderChange>
        {
            public FolderChange Current => inner.Current;

            public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();

            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync();

                finished.TrySetResult();
            }
        }
    }
}
