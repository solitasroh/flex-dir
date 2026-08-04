using FlexDir.Shell.Interop;

using Xunit;

namespace FlexDir.Shell.Tests.Interop;

/// <summary>
/// shell 호출을 STA 스레드에서 돌리는 큐.
/// <para>
/// 이 클래스의 존재 이유는 한 줄이다 — <c>SHGetFileInfo</c>·<c>IFileOperation</c>·
/// <c>IContextMenu</c> 는 STA 를 요구하고 스레드풀은 MTA 다
/// (docs/SHELL_NOTES.md §COM 아파트먼트). 그래서 첫 테스트가 아파트먼트를 잰다.
/// </para>
/// </summary>
public sealed class StaWorkQueueTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Work_RunsOnAnStaThread()
    {
        using var queue = new StaWorkQueue(1, "test");

        var state = await queue.RunAsync(() => Thread.CurrentThread.GetApartmentState(), CancellationToken.None)
            .WaitAsync(Limit);

        Assert.Equal(ApartmentState.STA, state);
    }

    // 스레드풀에서 돌면 이 테스트가 통과하지 못한다 — 그것이 이 큐가 있는 이유다.
    [Fact]
    public async Task Work_DoesNotRunOnTheCallingThread()
    {
        using var queue = new StaWorkQueue(1, "test");

        var id = await queue.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None)
            .WaitAsync(Limit);

        Assert.NotEqual(Environment.CurrentManagedThreadId, id);
    }

    [Fact]
    public async Task Result_ComesBack()
    {
        using var queue = new StaWorkQueue(1, "test");

        Assert.Equal(42, await queue.RunAsync(() => 42, CancellationToken.None).WaitAsync(Limit));
    }

    [Fact]
    public async Task Exception_Propagates()
    {
        using var queue = new StaWorkQueue(1, "test");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await queue.RunAsync<int>(() => throw new InvalidOperationException("실패"), CancellationToken.None)
                .WaitAsync(Limit));

        Assert.Equal("실패", error.Message);
    }

    [Fact]
    public async Task CanceledToken_DoesNotRunTheWork()
    {
        using var queue = new StaWorkQueue(1, "test");
        var ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.RunAsync(
                () => ran = true,
                new CancellationToken(canceled: true)).WaitAsync(Limit));

        Assert.False(ran);
    }

    // 앞선 작업이 네트워크 경로에서 초 단위로 멈춰 있는 동안(SHELL_NOTES §아이콘 함정 1)
    // 큐에서 기다리던 작업도 취소로 풀려야 한다. 그러지 않으면 폴더를 떠난 뒤에도
    // 그 요청이 살아서 워커를 붙잡는다.
    [Fact]
    public async Task QueuedWork_IsCanceledWhileWaiting()
    {
        using var queue = new StaWorkQueue(1, "test");
        using var block = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var blocking = queue.RunAsync(() => block.Wait(Limit), CancellationToken.None);
        var waiting = queue.RunAsync(() => 1, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting.WaitAsync(Limit));

        block.Set();
        await blocking.WaitAsync(Limit);
    }

    // 썸네일 조회는 항목마다 초 단위로 멈출 수 있다. 스레드가 하나면 그 하나가 막는 동안
    // 나머지가 전부 멈추므로, 호출자가 정한 동시 요청 수만큼 실제로 겹쳐 돌아야 한다.
    [Fact]
    public async Task MultipleThreads_RunWorkConcurrently()
    {
        using var queue = new StaWorkQueue(2, "test");
        using var both = new Barrier(2);

        var first = queue.RunAsync(() => both.SignalAndWait(Limit), CancellationToken.None);
        var second = queue.RunAsync(() => both.SignalAndWait(Limit), CancellationToken.None);

        // 겹치지 않으면 Barrier 가 서로를 기다리다 시간 안에 끝나지 않는다.
        await Task.WhenAll(first, second).WaitAsync(Limit);
    }

    [Fact]
    public async Task AfterDispose_WorkIsRejected()
    {
        var queue = new StaWorkQueue(1, "test");
        await queue.RunAsync(() => 1, CancellationToken.None).WaitAsync(Limit);

        queue.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await queue.RunAsync(() => 1, CancellationToken.None));
    }

    [Fact]
    public void ThreadCount_MustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StaWorkQueue(0, "test"));
}
