using System.Windows;

namespace FlexDir.App.Views;

/// <summary>
/// 탭을 전환하면 키보드 포커스가 <b>그 탭의 목록</b>으로 간다 (docs/PRD-v2.md §17 사용자
/// 렌즈 · docs/DESIGN.md §1-1).
/// <para>
/// 탭에 포커스가 남으면 방향키가 목록을 움직이지 않아 <b>고장으로 보인다</b>. 그것이 이
/// 파일이 있는 이유 전부다.
/// </para>
/// <para>
/// 신호가 <see cref="FrameworkElement.Loaded"/> 인 것은 소유 구조에서 따라 나온다 — 활성 탭이
/// 바뀌면 <c>ContentControl</c> 이 무는 <c>PaneViewModel</c> 이 바뀌고 페인 트리가 통째로 다시
/// 선다. 그러므로 "새 목록이 붙었다" 가 곧 "탭이 바뀌었다" 이고, 별도의 신호를 만들 필요가
/// 없다.
/// </para>
/// <para>
/// <b>판정은 그 페인이 활성인가 하나다.</b> 시작할 때 좌·우가 함께 서므로 그것이 없으면
/// 나중에 선 쪽이 포커스를 가져가 방향키가 보고 있지 않은 페인을 움직인다.
/// </para>
/// </summary>
public static class TabFocus
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(TabFocus),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(EnabledProperty, value);
    }

    /// <summary>
    /// 새로 선 목록이 포커스를 가져가야 하는가 — <b>그 페인이 활성일 때만</b>이다.
    /// <para>
    /// <c>PaneChrome.IsActive</c> 는 페인 루트에 한 번 걸려 상속으로 내려온다 (docs/DESIGN.md
    /// §6). 탭 클릭은 활성 전환을 <b>전환보다 먼저</b> 내므로 (<c>PaneTabsViewModel.ActivateTab</c>)
    /// 여기 닿을 때는 이미 참이다.
    /// </para>
    /// </summary>
    internal static bool ShouldTake(DependencyObject list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return PaneChrome.GetIsActive(list);
    }

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement list || args.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다. 목록은 탭마다 새로 서므로 이 구독도 그 목록과
        // 함께 사라진다 — 뗄 자리가 따로 없다.
        list.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        var list = (FrameworkElement)sender;

        // Focusable 을 확인하는 것은 ListInput 과 같은 이유다 — 못 받는 요소에 Focus() 를
        // 부르면 포커스가 어디에도 없는 상태로 남는다.
        if (list.Focusable && ShouldTake(list))
        {
            list.Focus();
        }
    }
}
