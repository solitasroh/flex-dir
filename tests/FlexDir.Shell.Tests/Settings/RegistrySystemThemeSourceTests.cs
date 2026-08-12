using FlexDir.Shell.Settings;

using Microsoft.Win32;

using Xunit;

namespace FlexDir.Shell.Tests.Settings;

/// <summary>
/// 실물 레지스트리를 읽는 <see cref="RegistrySystemThemeSource"/>.
/// <para>
/// 이 기계가 지금 라이트인지 다크인지는 세션마다 다르므로, 여기서 보는 것은 <b>같은 값을
/// 직접 다시 읽어도 같은 답이 나오는가</b>다 — <see cref="SystemDriveListTests"/> 와 같은
/// 구도다.
/// </para>
/// </summary>
public class RegistrySystemThemeSourceTests
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [Fact]
    public async Task ReadAsync_MatchesTheRegistryValueReadDirectly()
    {
        var source = new RegistrySystemThemeSource();

        var expected = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0;

        Assert.Equal(expected, await source.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_Cancelled_Throws()
    {
        var source = new RegistrySystemThemeSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.ReadAsync(cts.Token));
    }
}
