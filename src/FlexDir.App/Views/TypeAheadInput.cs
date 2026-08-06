using System.Windows;
using System.Windows.Input;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 목록에 들어온 문자 키를 <see cref="PaneViewModel.TypeAhead"/> 로 넘기는 attached behavior
/// (phase B-3 · docs/DESIGN.md §9).
/// <para>
/// v1 에 필터도 검색도 없으므로 큰 폴더에서 항목을 찾는 유일한 수단이다. 목록 컨트롤 내장
/// <c>TextSearch</c> 를 쓰지 않는 이유는 이동·선택과 같다 (ADR-016): wrap 뷰에서 컨테이너는
/// 항목이 아니라 <b>행</b>이라 내장 검색은 엉뚱한 것을 고른다.
/// </para>
/// <para>
/// 주소줄이 이 이벤트를 가져가지 않는다 — <c>TextInput</c> 은 포커스가 있는 곳에서 올라오고
/// 주소줄 <c>TextBox</c> 는 목록의 자손이 아니다.
/// </para>
/// <para>
/// 판정(<see cref="CharacterOf"/>)만 채점하고 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class TypeAheadInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(TypeAheadInput),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 접두어에 쌓을 문자. 문자가 아니면 <c>null</c> 이다 — 제어 문자를 쌓으면 그 뒤의 진짜
    /// 문자까지 아무것도 찾지 못한다. 공백은 문자다 (이름에 들어간다).
    /// </summary>
    internal static char? CharacterOf(string text)
        => text.Length == 0 || char.IsControl(text[0]) ? null : text[0];

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        element.TextInput += OnTextInput;
    }

    private static void OnTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PaneViewModel pane }
            && CharacterOf(args.Text) is { } character)
        {
            pane.TypeAhead(character);
            args.Handled = true;
        }
    }
}
