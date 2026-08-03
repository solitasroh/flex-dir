using FlexDir.Core.Locations;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.Core.Tests.Watching;

/// <summary>
/// <see cref="IFolderWatcher"/> 구현체가 반드시 만족해야 하는 성질.
/// <para>
/// 테스트 클래스가 아니라 기반 클래스다 (abstract 라서 xunit 이 수집하지 않는다).
/// <c>FlexDir.Shell</c> 의 <c>FileSystemWatcher</c> 기반 구현체 테스트가 이 클래스를
/// 상속해 같은 검증을 받는 것이 존재 이유다 — 그래서 여기서는 <see cref="IFolderWatcher"/>
/// 와 <see cref="CauseAsync"/> 만 본다. 구현체의 내부(채널·핸들)를 들여다보면 재사용이
/// 되지 않는다.
/// </para>
/// <para>
/// 모든 테스트에 타임아웃이 걸려 있다. 감시는 변경이 올 때까지 기다리는 스트림이므로
/// 종료 신호를 놓친 구현체는 <b>실패하지 않고 매달린다</b> — 그쪽이 더 나쁘다.
/// </para>
/// </summary>
public abstract class FolderWatcherContract
{
    /// <summary>
    /// 매달린 테스트를 실패로 바꾼다. 실제 대기 시간이 아니라 상한이므로 넉넉히 잡는다 —
    /// 정상 동작이면 이 시간에 닿지 않는다.
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <paramref name="folder"/> 를 감시하는 구현체를 낸다. 실제 구현체 테스트는
    /// 임시 디렉터리를 만들어 그 경로를 받으면 된다.
    /// </summary>
    protected abstract IFolderWatcher CreateWatcher(LocationId folder);

    /// <summary>
    /// 감시 중인 폴더에 <paramref name="change"/> 가 일어나게 만든다. fake 는 밀어넣고,
    /// 실제 구현체는 파일을 만들거나 이름을 바꾼다.
    /// <see cref="FolderChangeKind.Overflow"/> 는 "감시 버퍼를 넘치게 만든다" 는 뜻이다.
    /// <para>
    /// 계약이 이 훅을 요구하는 이유: 감시는 우리가 부르는 것이 아니라 외부에서 일어난다.
    /// 변경을 일으키는 방법은 구현체마다 다르지만 <b>무엇이 관측되어야 하는가</b>는 같다.
    /// </para>
    /// </summary>
    protected abstract Task CauseAsync(IFolderWatcher watcher, FolderChange change);

    // ── 취소 ────────────────────────────────────────────────────────
    // 감시 스트림은 폴더를 떠날 때마다 끝난다. 그것을 예외로 만들면 모든 폴더 전환이
    // 예외 경로가 된다 — 그래서 취소는 정상 종료다 (IFolderSource 와 다른 점).

    [Fact]
    public async Task CanceledToken_YieldsNothing_AndEnds()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var watcher = CreateWatcher(folder);

        var received = await WithTimeout(DrainAsync(watcher.WatchAsync(folder, Canceled())));

        Assert.Empty(received);
    }

    [Fact]
    public async Task Canceling_EndsTheStream_WithoutHanging()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var watcher = CreateWatcher(folder);

        using var cts = new CancellationTokenSource();

        // 변경이 오지 않는 동안 스트림은 열린 채 기다린다. 끝내는 신호는 취소뿐이다.
        var draining = Task.Run(() => DrainAsync(watcher.WatchAsync(folder, cts.Token)), CancellationToken.None);

        await cts.CancelAsync();

        // 여기서 TimeoutException 이 나면 취소가 스트림을 풀지 못한 것이다 —
        // 폴더를 옮길 때마다 감시가 하나씩 쌓인다.
        await WithTimeout(draining);
    }

    // ── 변경 종류 ───────────────────────────────────────────────────

    [Fact]
    public async Task Overflow_IsReported()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var watcher = CreateWatcher(folder);

        // 오버플로를 삼키면 목록이 stale 인 채로 남는다. 소비자에게 반드시 닿아야 한다.
        var change = await FirstChangeAsync(watcher, folder, FolderChange.Overflowed);

        Assert.Equal(FolderChangeKind.Overflow, change.Kind);
    }

    [Fact]
    public async Task Renamed_CarriesTheOldName()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var watcher = CreateWatcher(folder);

        var change = await FirstChangeAsync(
            watcher,
            folder,
            new FolderChange(FolderChangeKind.Renamed, "new.txt", "old.txt"));

        Assert.Equal(FolderChangeKind.Renamed, change.Kind);
        Assert.Equal("new.txt", change.Name);

        // 이름변경을 Removed + Added 로 쪼개면 선택이 풀린다 (ADR-011 — 갱신 중에도
        // 선택은 유지한다). 소비자가 같은 항목임을 알아보려면 이전 이름이 필요하다.
        Assert.Equal("old.txt", change.OldName);
    }

    /// <summary>
    /// 감시를 먼저 걸고 <paramref name="change"/> 를 일으켜 첫 변경 하나를 받는다.
    /// 순서가 중요하다 — 변경을 먼저 일으키면 실제 구현체는 그것을 놓친다.
    /// </summary>
    private async Task<FolderChange> FirstChangeAsync(IFolderWatcher watcher, LocationId folder, FolderChange change)
    {
        using var cts = new CancellationTokenSource();

        await using var changes = watcher.WatchAsync(folder, cts.Token).GetAsyncEnumerator();

        // 여기서 await 하지 않는다. 감시가 걸린 뒤에 변경이 일어나야 한다.
        var next = changes.MoveNextAsync().AsTask();

        await CauseAsync(watcher, change);

        Assert.True(await WithTimeout(next), "변경이 오지 않고 스트림이 끝났다.");
        var received = changes.Current;

        await cts.CancelAsync();
        return received;
    }

    private static async Task<List<FolderChange>> DrainAsync(IAsyncEnumerable<FolderChange> changes)
    {
        var received = new List<FolderChange>();

        await foreach (var change in changes)
        {
            received.Add(change);
        }

        return received;
    }

    private static Task<T> WithTimeout<T>(Task<T> task) => task.WaitAsync(Limit);

    protected static CancellationToken Canceled() => new(canceled: true);

    protected static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }
}
