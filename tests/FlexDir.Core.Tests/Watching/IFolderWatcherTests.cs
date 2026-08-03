using FlexDir.Core.Locations;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.Core.Tests.Watching;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이고
/// 계약 검증은 전부 <see cref="FolderWatcherContract"/> 에 있다 —
/// <c>FlexDir.Shell</c> 의 구현체 테스트도 같은 기반 클래스를 상속한다.
/// <para>
/// 아래 <see cref="FakeFolderWatcher"/> 전용 테스트는 계약이 아니라 주입 knob 을 본다.
/// 계약 쪽은 <see cref="IFolderWatcher"/> 만 보므로 여기가 fake 내부를 보는 유일한 자리다.
/// </para>
/// </summary>
public class FakeFolderWatcherTests : FolderWatcherContract
{
    protected override IFolderWatcher CreateWatcher(LocationId folder) => new FakeFolderWatcher();

    protected override Task CauseAsync(IFolderWatcher watcher, FolderChange change)
    {
        var fake = Assert.IsType<FakeFolderWatcher>(watcher);

        if (change.Kind == FolderChangeKind.Overflow)
        {
            fake.PushOverflow();
        }
        else
        {
            fake.Push(change);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task WatchCalls_RecordsFoldersInCallOrder()
    {
        var first = Folder(@"C:\A");
        var second = Folder(@"C:\B");
        var watcher = new FakeFolderWatcher();

        watcher.Complete();

        await Drain(watcher.WatchAsync(first, CancellationToken.None));
        await Drain(watcher.WatchAsync(second, CancellationToken.None));

        Assert.Equal([first, second], watcher.WatchCalls);
    }

    [Fact]
    public async Task PushBeforeAnyConsumer_IsBuffered()
    {
        var folder = Folder(@"C:\Temp");
        var watcher = new FakeFolderWatcher();

        watcher.Push(new FolderChange(FolderChangeKind.Added, "a.txt"));
        watcher.PushOverflow();
        watcher.Complete();

        var received = await Drain(watcher.WatchAsync(folder, CancellationToken.None));

        Assert.Equal(
            [new FolderChange(FolderChangeKind.Added, "a.txt"), FolderChange.Overflowed],
            received);
    }

    [Fact]
    public async Task Complete_EndsTheStreamWithoutCancellation()
    {
        var folder = Folder(@"C:\Temp");
        var watcher = new FakeFolderWatcher();

        watcher.Complete();

        Assert.Empty(await Drain(watcher.WatchAsync(folder, CancellationToken.None)));
        Assert.Equal(0, watcher.CancellationsObserved);
    }

    [Fact]
    public async Task Push_AfterComplete_Throws()
    {
        var watcher = new FakeFolderWatcher();
        watcher.Complete();

        Assert.Throws<InvalidOperationException>(() => watcher.Push(FolderChange.Overflowed));

        // 스트림은 이미 끝나 있다 — 위 예외는 fake 사용법의 오류일 뿐이다.
        Assert.Empty(await Drain(watcher.WatchAsync(Folder(@"C:\Temp"), CancellationToken.None)));
    }

    [Fact]
    public async Task CancellationsObserved_CountsTheCanceledWatch()
    {
        var folder = Folder(@"C:\Temp");
        var watcher = new FakeFolderWatcher();

        Assert.Equal(0, watcher.CancellationsObserved);

        Assert.Empty(await Drain(watcher.WatchAsync(folder, Canceled())));

        Assert.Equal(1, watcher.CancellationsObserved);
    }

    private static async Task<List<FolderChange>> Drain(IAsyncEnumerable<FolderChange> changes)
    {
        var received = new List<FolderChange>();

        await foreach (var change in changes)
        {
            received.Add(change);
        }

        return received;
    }
}
