using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlexDir.App.Views;

/// <summary>
/// <c>Ctrl+L</c>·<c>Alt+D</c> 로 그 페인의 주소줄에 포커스를 주는 attached behavior
/// (phase B-3 · docs/DESIGN.md §9).
/// <para>
/// 창이 아니라 <b>페인 루트</b>에 건다. 키는 포커스가 있는 곳에서 터널링해 내려오므로
/// 키를 받은 페인이 곧 사용자가 보고 있는 페인이다 — 활성 페인을 따로 물어볼 필요가 없고,
/// 창에 걸었을 때처럼 어느 쪽 주소줄인지 고르는 코드도 필요 없다.
/// </para>
/// <para>
/// 주소줄은 <see cref="IsAddressBoxProperty"/> 로 표시한다. 첫 <c>TextBox</c> 를 잡으면
/// B-4 의 이름변경 편집기가 같은 서브트리에 들어오는 순간 엉뚱한 곳으로 간다.
/// </para>
/// <para>
/// 판정(<see cref="IsAddressFocusKey"/>·<see cref="AddressBoxIn"/>)만 채점하고, 이벤트 훅과
/// 실제 포커스 이동은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class AddressFocus
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(AddressFocus),
        new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty IsAddressBoxProperty = DependencyProperty.RegisterAttached(
        "IsAddressBox",
        typeof(bool),
        typeof(AddressFocus),
        new PropertyMetadata(false, OnAddressBoxChanged));

    /// <summary>breadcrumb 목록의 표시. 빈 자리를 누르면 편집으로 들어간다.</summary>
    public static readonly DependencyProperty IsCrumbsProperty = DependencyProperty.RegisterAttached(
        "IsCrumbs",
        typeof(bool),
        typeof(AddressFocus),
        new PropertyMetadata(false, OnCrumbsChanged));

    /// <summary>
    /// 주소줄이 입력 상자인가. 페인 루트에서 <c>IsAddressEditing</c> 과 양방향으로 묶인다
    /// (<c>SplitterSync.Ratio</c> 와 같은 방식) — 그래서 이 파일은 ViewModel 타입을 모른다.
    /// </summary>
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.RegisterAttached(
        "IsEditing",
        typeof(bool),
        typeof(AddressFocus),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static bool GetIsCrumbs(DependencyObject element) => (bool)element.GetValue(IsCrumbsProperty);

    public static void SetIsCrumbs(DependencyObject element, bool value) => element.SetValue(IsCrumbsProperty, value);

    public static bool GetIsEditing(DependencyObject element) => (bool)element.GetValue(IsEditingProperty);

    public static void SetIsEditing(DependencyObject element, bool value) => element.SetValue(IsEditingProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetIsAddressBox(DependencyObject element) => (bool)element.GetValue(IsAddressBoxProperty);

    public static void SetIsAddressBox(DependencyObject element, bool value) => element.SetValue(IsAddressBoxProperty, value);

    /// <summary>주소줄로 가는 키인가. 둘 다 탐색기와 같다 (docs/DESIGN.md §9).</summary>
    internal static bool IsAddressFocusKey(Key key, ModifierKeys modifiers)
        => (key == Key.L && modifiers == ModifierKeys.Control)
            || (key == Key.D && modifiers == ModifierKeys.Alt);

    /// <summary>이 서브트리의 주소줄. 표시가 붙은 것만 찾는다.</summary>
    internal static TextBox? AddressBoxIn(DependencyObject root)
    {
        if (root is TextBox box && GetIsAddressBox(box))
        {
            return box;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (AddressBoxIn(VisualTreeHelper.GetChild(root, index)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        element.PreviewKeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        // Alt 조합은 Key 가 System 으로 오고 진짜 키는 SystemKey 에 있다.
        var key = args.Key == Key.System ? args.SystemKey : args.Key;

        if (!IsAddressFocusKey(key, Keyboard.Modifiers) || sender is not DependencyObject root)
        {
            return;
        }

        if (BeginEditing(root))
        {
            args.Handled = true;
        }
    }

    /// <summary>
    /// 주소줄을 편집 상태로 바꾸고 입력 상자에 포커스를 준다.
    /// <para>
    /// <b>포커스는 한 박자 뒤에 준다.</b> 평소에 보이는 것은 breadcrumb 이고 입력 상자는
    /// <c>Collapsed</c> 다 — 접힌 요소는 <see cref="UIElement.Focus"/> 를 받지 못한다.
    /// <see cref="IsEditingProperty"/> 를 켜면 바인딩이 ViewModel 을 지나 가시성 트리거까지
    /// 가는데 그 사이에 레이아웃이 한 번 돌아야 하므로, 그 뒤에 잡는다.
    /// </para>
    /// </summary>
    private static bool BeginEditing(DependencyObject root)
    {
        SetIsEditing(root, true);

        root.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (AddressBoxIn(root) is { IsVisible: true } address)
                {
                    address.Focus();

                    // 탐색기와 같다 — 바로 새 경로를 칠 수 있어야 한다.
                    address.SelectAll();
                }
            });

        return true;
    }

    private static void OnAddressBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true)
        {
            return;
        }

        // 편집을 떠나는 자리는 둘이다. 포커스를 잃으면(다른 페인 클릭 · 목록 클릭) 접고,
        // Esc 는 그 자리에서 접는다 — 이름변경 편집기와 같은 규약이다 (docs/DESIGN.md §9-1).
        box.LostKeyboardFocus += (sender, _) => EndEditing(sender);

        box.PreviewKeyDown += (sender, args) =>
        {
            if (args.Key != Key.Escape)
            {
                return;
            }

            // 되돌린다 — 쳐 놓은 글자가 breadcrumb 뒤에 남으면 다음 Ctrl+L 이 그것을 보여준다.
            if (sender is TextBox editor)
            {
                editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            }

            EndEditing(sender);
            args.Handled = true;
        };
    }

    private static void EndEditing(object sender)
    {
        if (sender is DependencyObject element && PaneRootOf(element) is { } root)
        {
            SetIsEditing(root, false);
        }
    }

    private static void OnCrumbsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement crumbs || e.NewValue is not true)
        {
            return;
        }

        // 칸이 없는 빈 자리를 누르면 편집이다 (탐색기와 같다). 칸 자체는 Button 이 먼저
        // 가져가므로 여기까지 오지 않는다.
        crumbs.MouseLeftButtonDown += (sender, args) =>
        {
            if (sender is DependencyObject element && PaneRootOf(element) is { } root)
            {
                BeginEditing(root);
                args.Handled = true;
            }
        };
    }

    /// <summary>
    /// <see cref="EnabledProperty"/> 가 켜진 조상. 편집 상태는 페인 루트가 들고 있다 —
    /// 그 자리가 ViewModel 과 바인딩된 유일한 지점이다.
    /// </summary>
    private static DependencyObject? PaneRootOf(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (GetEnabled(current))
            {
                return current;
            }
        }

        return null;
    }
}
