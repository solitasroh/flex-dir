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
/// </summary>
public sealed class FakeFolderWatcher : IFolderWatcher
{
    // 무한 채널이다. 소비자가 없을 때 밀어넣은 변경은 버퍼에 남아 다음 소비자가 받는다 —
    // "감시를 걸기 전에 일어난 변경" 을 테스트가 미리 준비할 수 있어야 한다.
    private readonly Channel<FolderChange> _changes = Channel.CreateUnbounded<FolderChange>();

    /// <summary><see cref="WatchAsync"/> 가 호출된 폴더의 순서.</summary>
    public List<LocationId> WatchCalls { get; } = [];

    /// <summary>취소가 관측된 횟수. 감시 스트림 하나가 취소로 끝날 때마다 1 늘어난다.</summary>
    public int CancellationsObserved { get; private set; }

    /// <summary>감시 중인 소비자에게 변경을 밀어넣는다.</summary>
    public void Push(FolderChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        // 조용히 삼키면 테스트가 "변경을 밀어넣었다" 고 믿은 채 매달린다.
        if (!_changes.Writer.TryWrite(change))
        {
            throw new InvalidOperationException("Complete 이후에는 변경을 밀어넣을 수 없다.");
        }
    }

    /// <summary>감시 버퍼 오버플로를 밀어넣는다.</summary>
    public void PushOverflow() => Push(FolderChange.Overflowed);

    /// <summary>스트림을 정상 종료시킨다. 감시 대상이 사라진 경우에 해당한다.</summary>
    public void Complete() => _changes.Writer.TryComplete();

    public IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // 호출 시점에 기록한다. 반복자 본문은 첫 MoveNextAsync 까지 실행되지 않으므로
        // 안에서 기록하면 "호출했는가" 가 아니라 "소비를 시작했는가" 를 재는 것이 된다.
        WatchCalls.Add(folder);

        return Watch(ct);
    }

    private async IAsyncEnumerable<FolderChange> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            FolderChange change;

            try
            {
                // ct 를 넘겨야 한다. CancellationToken.None 을 넘기면 변경이 오지 않는 동안
                // ReadAsync 가 풀리지 않고, 그러면 이 반복자를 dispose 하는 쪽도 영원히
                // 대기한다 — 테스트가 실패하지 않고 dotnet test 자체가 매달린다.
                change = await _changes.Reader.ReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // 취소는 정상 종료다 (계약). 예외로 내면 폴더 전환마다 예외 경로를 탄다.
                CancellationsObserved++;
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
}
