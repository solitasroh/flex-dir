using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 탭 줄의 마우스 입력을 커맨드로 넘기는 attached behavior — <b>가운데 버튼으로 닫기</b>와
/// <b>빈 곳 더블클릭으로 새 탭</b> (docs/PRD-v2.md §17 마우스).
/// <para>
/// 탭 클릭·닫기 <c>×</c>·<c>+</c>·우클릭 메뉴는 XAML 의 <c>Button</c>·<c>ContextMenu</c> 가
/// 그대로 받는다 — 여기 있는 둘은 그 길이 없다. <c>MouseBinding</c> 은 <c>Freezable</c> 이라
/// <c>DataContext</c> 를 상속받지 않고 (<c>ListInput</c> 이 행 템플릿에서 밟은 자리), 탭의
/// <c>DataContext</c> 는 탭 자신인데 커맨드는 그 부모에 있다.
/// </para>
/// <para>
/// <b>거는 자리는 <see cref="TabStripPanel"/> 이다</b> — 그 패널이 탭과 그 오른쪽의 빈 자리를
/// 함께 덮고, <c>+</c> 버튼은 그 밖에 있다. 줄 전체(<c>Border</c>)에 걸면 <c>+</c> 를 두 번
/// 누른 것이 '빈 곳 더블클릭' 으로도 잡혀 탭이 둘 생긴다.
/// </para>
/// </summary>
public static class TabStripInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(TabStripInput),
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
    /// 눌린 자리가 어느 탭인가. 탭 밖(줄의 빈 자리)이면 <see langword="null"/> 이고,
    /// 그 자리의 더블클릭이 '새 탭' 이다.
    /// </summary>
    internal static PaneViewModel? TabAt(DependencyObject strip, DependencyObject? origin)
        => ChromeAt(strip, origin)?.DataContext as PaneViewModel;

    /// <summary>
    /// 눌린 자리를 그리고 있는 <b>탭 하나의 시각 요소</b> — 줄의 직계 자식이다. 끌리는 탭을
    /// 반투명하게 만드는 대상이 이것이다 (<c>TabDragInput</c>).
    /// <para>
    /// 눌리는 것은 대개 제목 글자나 아이콘이라 시각 트리를 거슬러 올라가야 한다
    /// (<c>ListInput.ItemAt</c> 과 같은 수). 다만 멈추는 조건이 다르다 — <b>탭 안의 모든
    /// 요소가 탭의 <c>DataContext</c> 를 상속</b>하므로 "그 DataContext 를 가진 첫 요소" 로
    /// 멈추면 제목 글자에서 멈춘다. 줄의 직계 자식까지 올라가야 탭 자체다.
    /// </para>
    /// </summary>
    internal static FrameworkElement? ChromeAt(DependencyObject strip, DependencyObject? origin)
    {
        var node = origin;

        while (node is not null && node != strip)
        {
            // 시각 요소가 아니면 더 갈 수 없다 (GetParent 가 던진다).
            var parent = node is Visual ? VisualTreeHelper.GetParent(node) : null;

            if (parent == strip)
            {
                return node as FrameworkElement;
            }

            node = parent;
        }

        return null;
    }

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement strip || args.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다
        // (DragDropInput 과 같은 수).
        strip.PreviewMouseDown += OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var strip = (DependencyObject)sender;

        if (((FrameworkElement)sender).DataContext is not PaneTabsViewModel tabs)
        {
            return;
        }

        var tab = TabAt(strip, args.OriginalSource as DependencyObject);

        // 가운데 버튼은 그 탭을 닫는다. 마지막 탭과 고정 탭이면 페인이 거부한다.
        if (args.ChangedButton == MouseButton.Middle)
        {
            if (tab is not null && tabs.CloseTabCommand.CanExecute(tab))
            {
                args.Handled = true;

                tabs.CloseTabCommand.Execute(tab);
            }

            return;
        }

        // 빈 곳 더블클릭은 새 탭이다. 탭 위에서 난 더블클릭은 여기 오지 않는다 — 이름
        // 바꾸기의 진입은 컨텍스트 메뉴 하나뿐이고 (docs/PRD-v2.md §17), 탭 더블클릭을
        // 열어 두면 빈 곳과 몇 px 차이로 갈린다.
        if (args.ChangedButton == MouseButton.Left && args.ClickCount == 2 && tab is null)
        {
            args.Handled = true;

            tabs.AddTabCommand.Execute(null);
        }
    }
}
