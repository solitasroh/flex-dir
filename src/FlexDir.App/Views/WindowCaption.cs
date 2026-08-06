using System.Windows;
using System.Windows.Input;

namespace FlexDir.App.Views;

/// <summary>
/// 커스텀 타이틀바의 캡션 조작을 창에 거는 attached behavior
/// (docs/DESIGN.md §1 — 32px 크롬 · 사용자 결정 2026-08-06).
/// <para>
/// <b>우리가 만드는 것은 최소화·최대화/복원·닫기 셋뿐이다.</b> 끌어서 옮기기, 더블클릭
/// 최대화, Aero Snap, Win+방향키, 화면 위쪽으로 밀어 최대화는 <c>WindowChrome</c> 의
/// <c>CaptionHeight</c> 가 그 띠를 OS 캡션으로 넘기면 전부 그대로 온다. 직접 구현하면
/// 그 넷을 하나씩 되살려야 하고, 그것이 커스텀 크롬에서 회귀가 나는 자리다.
/// </para>
/// <para>
/// <b>버튼이 <c>SystemCommands</c> 를 쓰는데 왜 이 클래스가 필요한가</b>:
/// <c>SystemCommands</c> 는 커맨드 객체만 주고 실행은 창이 <c>CommandBinding</c> 으로
/// 붙여야 한다. 그 배선이 원래 가는 자리가 <c>*.xaml.cs</c> 이고 (CLAUDE.md §2 가 막는다)
/// 그래서 여기로 옮겼다 — <c>Views/</c> 의 다른 여덟 개와 같은 방식이다.
/// </para>
/// <para>
/// 닫기는 <c>Close()</c> 로 간다. 상주 프로세스(ADR-003)에서는 <c>ResidentWindow</c> 가
/// <c>Closing</c> 을 취소하고 숨기므로 <b>닫기 = 숨기기가 그대로 유지된다</b> — 여기서
/// <c>Hide()</c> 를 부르면 그 규약이 두 곳으로 갈린다.
/// </para>
/// </summary>
public static class WindowCaption
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(WindowCaption),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 최대화 버튼이 누른 뒤의 상태. 최대화면 풀고, 아니면 최대화한다.
    /// <para>
    /// 최소화가 <c>Normal</c> 이 아니라 <c>Maximized</c> 로 가는 이유: 되돌리는 값이
    /// "지금이 아닌 것" 이어야 트레이로 되살린 창이 다시 접히지 않는다.
    /// </para>
    /// </summary>
    internal static WindowState Toggled(WindowState current)
        => current == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다
        // (SplitterSync 와 같은 이유).
        Bind(window, SystemCommands.MinimizeWindowCommand, w => SystemCommands.MinimizeWindow(w));
        Bind(window, SystemCommands.MaximizeWindowCommand, w => w.WindowState = Toggled(w.WindowState));
        Bind(window, SystemCommands.CloseWindowCommand, w => w.Close());
    }

    private static void Bind(Window window, ICommand command, Action<Window> run)
    {
        window.CommandBindings.Add(
            new CommandBinding(command, (sender, _) => run((Window)sender)));
    }
}
