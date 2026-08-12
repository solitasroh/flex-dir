using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// VM 의 <c>IsDarkMode</c> 를 창의 리소스 사전에 실어 나르는 attached property
/// (docs/DESIGN.md §5 · docs/PRD-v2.md §19).
/// <para>
/// 실제 배선은 <c>v:ThemeSync.IsDarkMode="{Binding Settings.IsDarkMode}"</c> 로 XAML 에서
/// 건다. <see cref="FrameworkElement"/> 생성은 STA 가 필요해(<c>InputManager</c>) 여기서
/// 만들 수 없다 — <c>PaneChrome</c> 이 <see cref="DependencyObject"/> 로 그은 것과 같은 선이다.
/// 값이 바뀔 때 <see cref="ThemePalette.Apply"/> 가 실제로 색을 바꾸는지는
/// <see cref="ThemePaletteTests"/> 가 이미 채점했고, 창에 실제로 거는 것은 사람이 확인한다
/// (CLAUDE.md §5).
/// </para>
/// </summary>
public class ThemeSyncTests
{
    [Fact]
    public void IsDarkMode_DefaultsToFalse()
    {
        Assert.False(ThemeSync.GetIsDarkMode(new DependencyObject()));
    }

    [Fact]
    public void IsDarkMode_RoundTrips()
    {
        var element = new DependencyObject();

        ThemeSync.SetIsDarkMode(element, true);

        Assert.True(ThemeSync.GetIsDarkMode(element));
    }

    [Fact]
    public void IsDarkMode_SetOnSomethingWithNoResources_DoesNotThrow()
    {
        // FrameworkElement 가 아닌 DependencyObject 에 걸어도 죽지 않아야 한다 — 콜백이
        // 리소스 사전에 닿기 전에 타입을 가른다.
        ThemeSync.SetIsDarkMode(new DependencyObject(), true);
    }
}
