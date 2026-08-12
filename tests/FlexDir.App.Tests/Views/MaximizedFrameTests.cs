using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 최대화한 창이 작업영역 밖으로 나가는 몫을 재는 behavior.
/// <para>
/// 재는 판정만 여기서 채점한다 — 실제 창에 여백을 거는 것은 STA 가 필요해 사람이
/// 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class MaximizedFrameTests
{
    [Fact]
    public void Overflow_AMaximizedWindow_IsTheResizeFrameOnEverySide()
    {
        // 2026-08-12 주 모니터 실측. 최대화한 창 rect 는 작업영역보다 사방 8px 크다 —
        // 그만큼이 화면 밖이고, 위쪽 8px 이 캡션 줄을 잘라 먹던 몫이다.
        var overflow = MaximizedFrame.Overflow(
            window: new Rect(-8, -8, 2576, 1408),
            work: new Rect(0, 0, 2560, 1392));

        Assert.Equal(new Thickness(8, 8, 8, 8), overflow);
    }

    [Fact]
    public void Overflow_OnAMonitorThatDoesNotStartAtTheOrigin_IsStillTheFrame()
    {
        // 보조 세로 모니터는 (2560,-178) 에서 시작한다. 원점이 0 이 아니어도 같은 값이
        // 나와야 한다 — 여기서 좌표를 크기로 착각하면 두 번째 모니터에서만 틀린다.
        var overflow = MaximizedFrame.Overflow(
            window: new Rect(2552, -186, 1096, 1888),
            work: new Rect(2560, -178, 1080, 1872));

        Assert.Equal(new Thickness(8, 8, 8, 8), overflow);
    }

    [Fact]
    public void Overflow_AWindowInsideTheWorkArea_IsNothing()
    {
        // 보통 창은 덜어낼 것이 없다. 여기서 0 이 아니면 창을 옮길 때마다 내용이 움츠린다.
        var overflow = MaximizedFrame.Overflow(
            window: new Rect(2700, 100, 800, 900),
            work: new Rect(2560, -178, 1080, 1872));

        Assert.Equal(default, overflow);
    }

    [Fact]
    public void Overflow_AWindowHangingOffOneEdge_IsCountedOnThatEdgeOnly()
    {
        // 실측에서 나온 값이다 (2026-08-12) — 보통 창도 화면 밖으로 나가 있을 수 있다.
        // 재는 것은 순수 기하이고, 최대화가 아닐 때 이 값을 쓰지 않는 것은 부르는 쪽 몫이다.
        var overflow = MaximizedFrame.Overflow(
            window: new Rect(2700, 100, 1000, 900),
            work: new Rect(2560, -178, 1080, 1872));

        Assert.Equal(new Thickness(0, 0, 60, 0), overflow);
    }

    [Fact]
    public void Overflow_IsNeverNegative()
    {
        // 작업영역보다 작은 창은 음수 여백을 내는데, 음수 Margin 은 내용을 오히려 밖으로
        // 밀어낸다 (WPF 에서 예외가 아니라 그림이 깨지는 쪽으로 간다).
        var overflow = MaximizedFrame.Overflow(
            window: new Rect(100, 100, 200, 200),
            work: new Rect(0, 0, 2560, 1392));

        Assert.Equal(default, overflow);
    }
}
