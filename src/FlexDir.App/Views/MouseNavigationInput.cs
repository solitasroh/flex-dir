using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 마우스 보조 버튼(4·5번) → 뒤로·앞으로 (docs/PRD-v2.md §15 · 사용자 요청 2026-08-11).
/// <para>
/// <b><see cref="MouseBinding"/> 으로는 쓸 수 없다.</b> <see cref="MouseAction"/> 에는
/// 왼쪽·오른쪽·가운데·휠만 있고 XButton 이 없다 — 창의 키보드 맵처럼
/// <c>InputBindings</c> 에 한 줄 더하는 길이 막혀 있다 (docs/DESIGN.md §9). 그래서 창에 한 번
/// 걸고 <see cref="MouseButtonEventArgs.ChangedButton"/> 을 본다.
/// </para>
/// <para>
/// <b>창에 거는 이유는 페인 밖에서도 눌린다는 것이다.</b> 트리·툴바 위에서 누른 것은
/// <see cref="PaneAt"/> 가 <c>null</c> 을 내고 활성 페인으로 간다
/// (<c>WorkspaceViewModel.GoBackAtAsync</c>).
/// </para>
/// <para>
/// 판정 둘(<see cref="IsBack"/>·<see cref="PaneAt"/>)만 채점한다. 이벤트 훅과 실제 버튼은
/// 사람이 확인한다 (CLAUDE.md §5) — 보조 버튼은 UIA 로 만들 수 없고, 드라이버에 따라
/// <c>WM_XBUTTONDOWN</c> 이 아니라 <c>WM_APPCOMMAND</c> 로 오는 기계도 있다. 후자는 WPF 가
/// 이벤트로 올리지 않으므로 실물에서 안 먹으면 그때 <c>HwndSource</c> 훅을 세운다.
/// </para>
/// </summary>
public static class MouseNavigationInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(MouseNavigationInput),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 뒤로인가. <b>4번이 뒤로</b>다 — 브라우저·탐색기가 그 매핑이라(<c>XBUTTON1</c>)
    /// 반대로 붙이면 손이 매번 틀린다.
    /// </summary>
    internal static bool IsBack(MouseButton button) => button == MouseButton.XButton1;

    /// <inheritdoc cref="IsBack"/>
    internal static bool IsForward(MouseButton button) => button == MouseButton.XButton2;

    /// <summary>
    /// 눌린 자리가 속한 페인. 페인 밖이면 <c>null</c> 이다 (트리·툴바·상태표시줄).
    /// <para>
    /// 행·칸은 자기 DataContext 를 가지므로 <see cref="PaneViewModel"/> 이 나올 때까지
    /// 올라간다 (<c>ListInput.ItemAt</c> 과 같은 걸음).
    /// </para>
    /// </summary>
    internal static PaneViewModel? PaneAt(DependencyObject root, DependencyObject? origin)
    {
        var node = origin;

        while (node is not null && node != root)
        {
            if (node is FrameworkElement { DataContext: PaneViewModel pane })
            {
                return pane;
            }

            // 시각 요소가 아니면 더 갈 수 없다 (GetParent 가 던진다).
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return null;
    }

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement root || args.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        // 터널링으로 받는다: 어느 컨트롤 위에서 눌러도 창이 먼저 보므로 판정이 하나다.
        root.PreviewMouseDown += OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var root = (FrameworkElement)sender;
        var back = IsBack(args.ChangedButton);

        if ((!back && !IsForward(args.ChangedButton))
            || root.DataContext is not WorkspaceViewModel workspace)
        {
            return;
        }

        args.Handled = true;

        var under = PaneAt(root, args.OriginalSource as DependencyObject);

        // 기다리지 않는다 — 여는 것은 저장소에 닿고 (CLAUDE.md §3) 실패 사유는 페인이
        // 상태표시줄에 올린다.
        _ = back ? workspace.GoBackAtAsync(under) : workspace.GoForwardAtAsync(under);
    }
}
