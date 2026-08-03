using FlexDir.App.Tests.Fakes;
using FlexDir.App.Threading;

using Xunit;

namespace FlexDir.App.Tests.Threading;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이고,
/// 실제 <c>Dispatcher</c> 기반 구현체도 같은 검증을 받는다 (수동 UI phase).
/// </summary>
public class InlineUiDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_RunsTheAction()
    {
        var dispatcher = new InlineUiDispatcher();
        var ran = false;

        await dispatcher.InvokeAsync(() => ran = true);

        Assert.True(ran);
        Assert.Equal(1, dispatcher.Invocations);
    }

    [Fact]
    public async Task InvokeAsync_RunsActionsInOrder()
    {
        var dispatcher = new InlineUiDispatcher();
        var order = new List<int>();

        await dispatcher.InvokeAsync(() => order.Add(1));
        await dispatcher.InvokeAsync(() => order.Add(2));

        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void IsOnUiThread_IsTrue()
    {
        // 인라인 실행이다 — 넘긴 동작은 부른 그 스레드에서 돈다.
        Assert.True(new InlineUiDispatcher().IsOnUiThread);
    }

    [Fact]
    public async Task IsInvoking_IsTrueOnlyInsideTheAction()
    {
        var dispatcher = new InlineUiDispatcher();

        Assert.False(dispatcher.IsInvoking);

        var insideAction = false;
        await dispatcher.InvokeAsync(() => insideAction = dispatcher.IsInvoking);

        Assert.True(insideAction);
        Assert.False(dispatcher.IsInvoking);
    }

    [Fact]
    public async Task InvokeAsync_FaultsTheTask_WhenTheActionThrows()
    {
        var dispatcher = new InlineUiDispatcher();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await dispatcher.InvokeAsync(() => throw new InvalidOperationException("터졌다")));

        // 예외로 끝나도 상태가 새는 것을 남기지 않는다.
        Assert.False(dispatcher.IsInvoking);
    }

    [Fact]
    public async Task InvokeAsync_IsUsableThroughThePortAlone()
    {
        IUiDispatcher port = new InlineUiDispatcher();
        var ran = false;

        await port.InvokeAsync(() => ran = true);

        Assert.True(ran);
        Assert.True(port.IsOnUiThread);
    }

    [Fact]
    public async Task InvokeAsync_NullAction_Throws()
    {
        var dispatcher = new InlineUiDispatcher();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await dispatcher.InvokeAsync(null!));
    }
}
