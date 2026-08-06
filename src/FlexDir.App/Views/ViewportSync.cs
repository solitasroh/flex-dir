using System.Windows;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 목록 패널의 크기를 <see cref="PaneViewModel.SetViewportSize"/> 로 미는 attached behavior
/// (phase B-3 · ADR-016).
/// <para>
/// <b>목록 컨트롤이 아니라 <c>ItemsPanel</c> 에 건다.</b> 가상화 패널이 스크롤 주인
/// (<c>IScrollInfo</c>)이라 그 <c>RenderSize</c> 가 곧 픽셀 뷰포트다 — <c>ListBox</c> 의
/// 크기에는 스크롤바가 들어 있어 마지막 칸이 잘리고, <c>ScrollViewer</c> 의
/// <c>Viewport*</c> 는 <c>ScrollUnit="Item"</c> 아래에서 스크롤 축이 픽셀이 아니라 항목
/// 수다 (PageUp/Down 이 그 축의 픽셀을 쓴다).
/// </para>
/// <para>
/// 뷰 모드가 바뀔 때는 밀지 않아도 된다 — 같은 패널이 다시 쓰이고, 크기를 아는 쪽인
/// <see cref="PaneViewModel.ViewMode"/> 가 그때 열 수를 다시 계산한다.
/// </para>
/// <para>
/// 판정(<see cref="IsUsable"/>)만 채점하고 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class ViewportSync
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(ViewportSync),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 밀 수 있는 크기인가. 레이아웃 전의 0 과 무한 제약 아래에서 측정된 값은 열 수를
    /// 망가뜨린다 — 0 은 줄당 1개로 접었다가 되돌리는 재배치를 한 번 더 만들고, 무한대는
    /// <c>int</c> 로 접히며 음수 열 수가 된다.
    /// </summary>
    internal static bool IsUsable(double width, double height)
        => double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 ItemsPanelTemplate 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도
        // 없다. 뷰를 바꿔 패널이 새로 만들어지면 그 패널이 자기 핸들러를 든다.
        element.SizeChanged += (sender, _) => Push((FrameworkElement)sender);
    }

    private static void Push(FrameworkElement panel)
    {
        if (panel.DataContext is PaneViewModel pane && IsUsable(panel.ActualWidth, panel.ActualHeight))
        {
            pane.SetViewportSize(panel.ActualWidth, panel.ActualHeight);
        }
    }
}
