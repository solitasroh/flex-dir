using System.Windows;
using System.Windows.Media;

namespace FlexDir.App.Views;

/// <summary>
/// 색 토큰 16개(docs/DESIGN.md §5)를 라이트·다크로 바꿔 끈다.
///
/// <para>
/// <b>리소스 항목을 새 브러시로 교체한다.</b> 같은 인스턴스의
/// <see cref="SolidColorBrush.Color"/> 를 바꾸는 길은 막혀 있다 — <b>XAML/BAML 로 로드된
/// 브러시는 frozen</b> 이라 값을 쓰면 <see cref="InvalidOperationException"/> 이다. 그래서
/// <c>MainWindow.xaml</c> 의 브러시 참조는 전부 <c>DynamicResource</c> 다: 사전 항목이 바뀌면
/// 참조하는 쪽이 따라온다.
/// </para>
///
/// <para>
/// <b>처음에는 반대로 갔고 실물에서 틀렸다</b> (2026-08-12 · docs/ADR.md ADR-020 §고친 것).
/// "XAML 리소스는 자동으로 <c>Freeze()</c> 되지 않는다" 를 근거로 <c>StaticResource</c> 264곳을
/// 그대로 두고 <c>Color</c> 만 바꿨는데, BAML 최적화가 정적 값만 가진 Freezable 을 freeze
/// 한다. 게이트 4종을 통과했고(테스트가 <b>코드로 만든</b> 브러시를 썼다 — 그것은 frozen 이
/// 아니다) 실물에서 예외가 났으며, §14 핸들러가 그것을 삼켜 <b>화면만 조용히 라이트로</b>
/// 남았다.
/// </para>
///
/// <para>
/// 값의 출처는 Windows 11 탐색기가 쓰는 WinUI 다크 중립 팔레트의 근사치다
/// (docs/ADR.md ADR-020 · 사용자 결정 2026-08-12). 실물 대조는 하지 않았다 — 그것은
/// 사람이 볼 몫이다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class ThemePalette
{
    public static readonly IReadOnlyDictionary<string, Color> Light = new Dictionary<string, Color>
    {
        ["AccentBrush"] = Color.FromRgb(0x00, 0x67, 0xC0),
        ["WinBgBrush"] = Color.FromRgb(0xF3, 0xF3, 0xF3),
        ["ChromeBgBrush"] = Color.FromRgb(0xF9, 0xF9, 0xF9),
        ["PaneBgBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["TextBrush"] = Color.FromRgb(0x1B, 0x1B, 0x1B),
        ["Text2Brush"] = Color.FromRgb(0x61, 0x61, 0x61),
        ["TextDisabledBrush"] = Color.FromRgb(0x9D, 0x9D, 0x9D),
        ["StrokeBrush"] = Color.FromRgb(0xE5, 0xE5, 0xE5),
        ["StrokeStrongBrush"] = Color.FromRgb(0xD0, 0xD0, 0xD0),
        ["HoverBrush"] = Color.FromArgb(0x0C, 0x00, 0x00, 0x00),
        ["PressedBrush"] = Color.FromArgb(0x14, 0x00, 0x00, 0x00),
        ["SelBrush"] = Color.FromArgb(0x24, 0x00, 0x67, 0xC0),
        ["SelHoverBrush"] = Color.FromArgb(0x33, 0x00, 0x67, 0xC0),
        ["SelInactiveBrush"] = Color.FromArgb(0x12, 0x00, 0x00, 0x00),
        ["FocusRingBrush"] = Color.FromRgb(0x1B, 0x1B, 0x1B),
        ["ErrorBrush"] = Color.FromRgb(0xC4, 0x2B, 0x1C),
    };

    public static readonly IReadOnlyDictionary<string, Color> Dark = new Dictionary<string, Color>
    {
        ["AccentBrush"] = Color.FromRgb(0x4C, 0xC2, 0xFF),
        ["WinBgBrush"] = Color.FromRgb(0x20, 0x20, 0x20),
        ["ChromeBgBrush"] = Color.FromRgb(0x2C, 0x2C, 0x2C),
        ["PaneBgBrush"] = Color.FromRgb(0x1F, 0x1F, 0x1F),
        ["TextBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["Text2Brush"] = Color.FromRgb(0xC5, 0xC5, 0xC5),
        ["TextDisabledBrush"] = Color.FromRgb(0x6B, 0x6B, 0x6B),
        ["StrokeBrush"] = Color.FromRgb(0x3B, 0x3B, 0x3B),
        ["StrokeStrongBrush"] = Color.FromRgb(0x4D, 0x4D, 0x4D),
        ["HoverBrush"] = Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF),
        ["PressedBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        ["SelBrush"] = Color.FromArgb(0x24, 0x4C, 0xC2, 0xFF),
        ["SelHoverBrush"] = Color.FromArgb(0x33, 0x4C, 0xC2, 0xFF),
        ["SelInactiveBrush"] = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
        ["FocusRingBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["ErrorBrush"] = Color.FromRgb(0xFF, 0x68, 0x59),
    };

    /// <summary>
    /// <paramref name="resources"/> 안의 16개 브러시를 <paramref name="isDarkMode"/> 색으로
    /// 교체한다. <b>없는 키는 만들지 않는다</b> — 엉뚱한 사전에 걸렸을 때 색 토큰을 새로 심는
    /// 것은 고치기 어려운 사고다. 컨버터·스타일 등 다른 항목은 건드리지 않는다.
    /// </summary>
    public static void Apply(ResourceDictionary resources, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var table = isDarkMode ? Dark : Light;

        foreach (var (key, color) in table)
        {
            if (!resources.Contains(key))
            {
                continue;
            }

            // 새 인스턴스로 교체한다 (위 §요약 — frozen 이라 Color 를 쓸 수 없다).
            // 만들면서 freeze 한다: 여러 요소가 공유하는 브러시라 그편이 렌더링에 낫고,
            // 다음 전환은 또 교체이므로 잃는 것이 없다.
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            resources[key] = brush;
        }
    }
}
