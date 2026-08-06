using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        new PropertyMetadata(false));

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

        if (!IsAddressFocusKey(key, Keyboard.Modifiers) || AddressBoxIn((DependencyObject)sender) is not { } address)
        {
            return;
        }

        address.Focus();

        // 탐색기와 같다 — 바로 새 경로를 칠 수 있어야 한다.
        address.SelectAll();

        args.Handled = true;
    }
}
