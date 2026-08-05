using System.Windows.Threading;

using FlexDir.App.Threading;

using Xunit;

namespace FlexDir.App.Tests.Threading;

/// <summary>
/// <see cref="IUiDispatcher"/> 의 실물. WPF <see cref="Dispatcher"/> 를 감싼다.
/// <para>
/// <c>Application</c> 없이도 잰다 — <see cref="Dispatcher.CurrentDispatcher"/> 는 스레드마다
/// 만들어지므로 전용 스레드 하나에 <see cref="Dispatcher.Run"/> 을 돌리면 실제 펌프가 있는
/// UI 스레드가 된다. 이것이 없으면 이 어댑터의 유일한 존재 이유(스레드를 정말 옮기는가)를
/// 확인할 수 없다.
/// </para>
/// <para>
/// 모든 대기에 시한을 준다. 펌프가 멈춘 경우를 매달림이 아니라 실패로 만들어야 진단이
/// 된다 (.harness/HANDOFF.md §규칙 4).
/// </para>
/// </summary>
public class WpfUiDispatcherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task InvokeAsync_FromAnotherThread_RunsOnTheDispatcherThread()
    {
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);
        var ran = 0;

        await dispatcher.InvokeAsync(() => ran = Environment.CurrentManagedThreadId).WaitAsync(Timeout);

        Assert.Equal(ui.ThreadId, ran);
        Assert.NotEqual(Environment.CurrentManagedThreadId, ran);
    }

    [Fact]
    public async Task InvokeAsync_OnTheDispatcherThread_RunsInlineAndDoesNotQueue()
    {
        // 큐에 넣으면 폴더 하나를 여는 데 왕복이 수백 번 생기고, UI 스레드에서 그 완료를
        // 기다리면 자기 큐를 기다리는 교착이 된다.
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);
        var completedInline = false;

        await ui.RunAsync(() =>
        {
            var ran = false;
            var task = dispatcher.InvokeAsync(() => ran = true);

            completedInline = ran && task.IsCompletedSuccessfully;
        }).WaitAsync(Timeout);

        Assert.True(completedInline);
    }

    [Fact]
    public async Task IsOnUiThread_IsTrueOnlyOnTheDispatcherThread()
    {
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);
        var onUiThread = false;

        await ui.RunAsync(() => onUiThread = dispatcher.IsOnUiThread).WaitAsync(Timeout);

        Assert.True(onUiThread);
        Assert.False(dispatcher.IsOnUiThread);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheActionThrows_FaultsTheTask()
    {
        // 목록 갱신에서 터진 예외를 삼키면 화면과 파일시스템이 어긋난 채로 남는다.
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);

        var task = dispatcher.InvokeAsync(() => throw new InvalidOperationException("갱신 실패"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(Timeout));
        Assert.Equal("갱신 실패", error.Message);
    }

    [Fact]
    public async Task InvokeAsync_OnTheDispatcherThread_WhenTheActionThrows_FaultsTheTask()
    {
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);
        Task? inline = null;

        await ui.RunAsync(() => inline = dispatcher.InvokeAsync(
            () => throw new InvalidOperationException("갱신 실패"))).WaitAsync(Timeout);

        Assert.NotNull(inline);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inline!.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeAsync_AfterShutdown_DoesNothingInsteadOfThrowing()
    {
        // 완전 종료 경로가 여기를 지난다 (ADR-003). 예외로 만들면 ViewModel 정리가 거기서
        // 멈추고 STA 워커가 남는다.
        var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);
        ui.Dispose();

        var ran = false;
        await dispatcher.InvokeAsync(() => ran = true).WaitAsync(Timeout);

        Assert.False(ran);
    }

    [Fact]
    public void Ctor_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => new WpfUiDispatcher(null!));

    [Fact]
    public void InvokeAsync_Null_Throws()
    {
        using var ui = new DispatcherThread();
        var dispatcher = new WpfUiDispatcher(ui.Dispatcher);

        // 인자 검사는 Task 를 만들기 전에 해야 한다 — 던지는 Task 로 미루면 호출자가
        // 기다리지 않는 경로에서 조용히 사라진다.
        Assert.Throws<ArgumentNullException>(() => { _ = dispatcher.InvokeAsync(null!); });
    }

    /// <summary>펌프가 도는 UI 스레드 하나. <c>Application</c> 없이 만든다.</summary>
    private sealed class DispatcherThread : IDisposable
    {
        private readonly Thread thread;

        public DispatcherThread()
        {
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

            thread = new Thread(() =>
            {
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            })
            {
                // 테스트가 실패해도 프로세스를 붙잡지 않는다.
                IsBackground = true,
                Name = "flex-dir test ui",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Dispatcher = ready.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
            ThreadId = thread.ManagedThreadId;
        }

        public Dispatcher Dispatcher { get; }

        public int ThreadId { get; }

        public Task RunAsync(Action action) => Dispatcher.InvokeAsync(action).Task;

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            thread.Join(Timeout);
        }
    }
}
