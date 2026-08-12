using System.Windows;

namespace FlexDir.App.Views;

/// <summary>
/// VM 의 <c>IsDarkMode</c> 를 창의 리소스 사전에 실어 나르는 attached property
/// (docs/DESIGN.md §5 · docs/PRD-v2.md §19).
/// <para>
/// <c>v:ThemeSync.IsDarkMode="{Binding Settings.IsDarkMode}"</c> 로 <c>MainWindow</c> 루트에
/// 건다. 값이 바뀔 때마다(사용자가 라디오를 바꾸거나, 시스템 모드에서 OS 설정이 바뀔 때)
/// <see cref="ThemePalette.Apply"/> 를 부른다 — 색 그 자체의 규칙은 <see cref="ThemePalette"/>
/// 가 쥔다.
/// </para>
/// </summary>
public static class ThemeSync
{
    public static readonly DependencyProperty IsDarkModeProperty = DependencyProperty.RegisterAttached(
        "IsDarkMode",
        typeof(bool),
        typeof(ThemeSync),
        new PropertyMetadata(false, OnIsDarkModeChanged));

    public static bool GetIsDarkMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(IsDarkModeProperty);
    }

    public static void SetIsDarkMode(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IsDarkModeProperty, value);
    }

    private static void OnIsDarkModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            ThemePalette.Apply(element.Resources, (bool)e.NewValue);
        }
    }
}
