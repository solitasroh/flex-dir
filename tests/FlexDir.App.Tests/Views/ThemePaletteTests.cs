using System.Windows;
using System.Windows.Media;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 색 토큰 16개를 라이트·다크로 바꿔 끄는 <see cref="ThemePalette"/> (docs/DESIGN.md §5).
///
/// <para>
/// <b>브러시를 <see cref="ResourceDictionary"/> 항목째로 교체한다</b> — 같은 인스턴스의
/// <see cref="SolidColorBrush.Color"/> 를 바꾸는 것이 아니다. XAML/BAML 로 로드된 브러시는
/// <b>frozen</b> 이라 값을 쓸 수 없다 (docs/ADR.md ADR-020 §고친 것). 그래서
/// <c>MainWindow.xaml</c> 의 참조가 <c>DynamicResource</c> 다 — 사전 항목이 바뀌면 따라온다.
/// </para>
///
/// <para>
/// <b>여기서 seed 를 반드시 <see cref="Freezable.Freeze()"/> 한다.</b> 2026-08-12 에 이
/// 파일이 <c>new SolidColorBrush(...)</c> 를 그대로 넣어 채점했고, 코드로 만든 브러시는
/// frozen 이 아니라서 **실물과 다른 조건**을 봤다 — 게이트 4종을 통과하고 실물에서
/// <see cref="InvalidOperationException"/> 이 났다. 실물 조건을 재현하는 것이 이 테스트의
/// 본체다.
/// </para>
/// </summary>
public class ThemePaletteTests
{
    /// <summary>XAML 이 만드는 것과 같은 조건 — 라이트 색을 담은 <b>frozen</b> 브러시들.</summary>
    private static ResourceDictionary SeedLikeXaml()
    {
        var resources = new ResourceDictionary();

        foreach (var (key, color) in ThemePalette.Light)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        return resources;
    }

    private static Color ColorOf(ResourceDictionary resources, string key)
        => ((SolidColorBrush)resources[key]).Color;

    [Fact]
    public void Apply_ToDark_GivesEveryTokenTheDarkColor()
    {
        var resources = SeedLikeXaml();

        ThemePalette.Apply(resources, isDarkMode: true);

        foreach (var (key, color) in ThemePalette.Dark)
        {
            Assert.Equal(color, ColorOf(resources, key));
        }
    }

    [Fact]
    public void Apply_ToLight_RestoresEveryTokenToTheLightColor()
    {
        var resources = SeedLikeXaml();

        ThemePalette.Apply(resources, isDarkMode: true);
        ThemePalette.Apply(resources, isDarkMode: false);

        foreach (var (key, color) in ThemePalette.Light)
        {
            Assert.Equal(color, ColorOf(resources, key));
        }
    }

    [Fact]
    public void Apply_OverFrozenBrushes_DoesNotThrow()
    {
        // **이것이 실물에서 터진 자리다** (2026-08-12 · error.log). frozen 브러시의 Color 에
        // 쓰면 InvalidOperationException 이고, §14 핸들러가 그것을 삼켜 화면만 조용히
        // 라이트로 남았다.
        var resources = SeedLikeXaml();

        Assert.True(((SolidColorBrush)resources["AccentBrush"]).IsFrozen, "seed 가 실물 조건이 아니다");

        ThemePalette.Apply(resources, isDarkMode: true);
    }

    [Fact]
    public void Apply_ReplacesTheEntry_SoDynamicResourceReferencesFollow()
    {
        // 인스턴스를 바꾸는 것이 이 구현의 방식이다 — 그래서 XAML 참조가 DynamicResource 여야
        // 한다. StaticResource 는 파싱 시점의 인스턴스를 쥐어 갱신되지 않는다.
        var resources = SeedLikeXaml();
        var before = resources["AccentBrush"];

        ThemePalette.Apply(resources, isDarkMode: true);

        Assert.NotSame(before, resources["AccentBrush"]);
    }

    [Fact]
    public void Apply_LeavesUnknownKeysAlone()
    {
        // MainWindow.xaml 의 리소스 사전에는 색 토큰 말고도 컨버터·스타일이 섞여 있다.
        var resources = SeedLikeXaml();
        var converter = new object();
        resources["SomeConverter"] = converter;

        ThemePalette.Apply(resources, isDarkMode: true);

        Assert.Same(converter, resources["SomeConverter"]);
    }

    [Fact]
    public void Apply_ToADictionaryWithoutTheTokens_DoesNotInventThem()
    {
        // 엉뚱한 사전에 걸렸을 때 키를 만들어 넣지 않는다 — 없는 것은 없는 채로 둔다.
        var resources = new ResourceDictionary();

        ThemePalette.Apply(resources, isDarkMode: true);

        Assert.False(resources.Contains("AccentBrush"));
    }

    [Fact]
    public void LightAndDark_HaveTheSameSixteenKeys()
    {
        // 하나가 빠지면 그 토큰만 다크에서 라이트 값에 갇힌다.
        Assert.Equal(16, ThemePalette.Light.Count);
        Assert.Equal(ThemePalette.Light.Keys.OrderBy(k => k), ThemePalette.Dark.Keys.OrderBy(k => k));
    }
}
