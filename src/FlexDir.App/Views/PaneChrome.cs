using System.Windows;

namespace FlexDir.App.Views;

/// <summary>
/// "이 서브트리는 활성 페인인가" 를 실어 나르는 attached property (docs/DESIGN.md §6).
/// <para>
/// 페인 루트에 <c>ActiveSide</c> 바인딩으로 한 번만 걸면 <b>상속</b>으로 행 템플릿까지
/// 내려간다 — 비활성 페인의 선택색 강등과 포커스 테두리 숨김이 이 값 하나로 갈린다.
/// 코드비하인드가 금지라 (CLAUDE.md §2) UserControl 의 DP 로 둘 수 없어 여기 있다.
/// </para>
/// </summary>
public static class PaneChrome
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(PaneChrome),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(IsActiveProperty);
    }

    public static void SetIsActive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IsActiveProperty, value);
    }
}
