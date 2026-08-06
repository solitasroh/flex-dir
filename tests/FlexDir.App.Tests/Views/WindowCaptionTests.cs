using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 커스텀 타이틀바의 캡션 조작 (docs/DESIGN.md §1 · 사용자 결정 2026-08-06).
/// <para>
/// <b>드래그·더블클릭 최대화·스냅은 우리 코드가 아니다.</b> <c>WindowChrome.CaptionHeight</c>
/// 가 그 띠를 OS 의 캡션으로 넘기므로 끌기·Aero Snap·Win+방향키가 그대로 온다. 우리가
/// 정하는 것은 최대화/복원 토글 하나뿐이고 여기서 채점하는 것도 그것뿐이다 — 실제 창은
/// STA 와 메시지 펌프가 필요해 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class WindowCaptionTests
{
    [Fact]
    public void Toggled_ANormalWindow_Maximizes()
    {
        Assert.Equal(WindowState.Maximized, WindowCaption.Toggled(WindowState.Normal));
    }

    [Fact]
    public void Toggled_AMaximizedWindow_Restores()
    {
        Assert.Equal(WindowState.Normal, WindowCaption.Toggled(WindowState.Maximized));
    }

    [Fact]
    public void Toggled_AMinimizedWindow_Maximizes_AndDoesNotStayMinimized()
    {
        // 최소화된 창에서는 이 버튼이 보이지 않지만, 상태를 그대로 되돌려주면 트레이로
        // 되살린 창이 다시 최소화된다. 되돌리는 값이 "지금이 아닌 것" 이어야 한다.
        Assert.Equal(WindowState.Maximized, WindowCaption.Toggled(WindowState.Minimized));
    }
}
