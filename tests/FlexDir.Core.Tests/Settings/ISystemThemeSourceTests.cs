using FlexDir.Core.Settings;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Settings;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>RegistrySystemThemeSource</c>)는
/// <c>FlexDir.Shell</c> 의 몫이다 — <see cref="Storage.IDriveList"/> 와 같은 구도다.
/// </summary>
public class FakeSystemThemeSourceTests
{
    [Fact]
    public async Task ReadAsync_GivesWhatWasPutIn()
    {
        var source = new FakeSystemThemeSource { IsDarkMode = true };

        Assert.True(await source.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_DefaultsToLight()
    {
        // OS 의 기본값이 라이트다 — 읽지 못했을 때 조용히 다크로 접히면 안 된다.
        Assert.False(await new FakeSystemThemeSource().ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_CountsHowManyTimesItWasAsked()
    {
        // WM_SETTINGCHANGE 마다 다시 묻는다 — 캐시해서 한 번만 읽는 것이 아니어야 한다.
        var source = new FakeSystemThemeSource();

        await source.ReadAsync(CancellationToken.None);
        await source.ReadAsync(CancellationToken.None);

        Assert.Equal(2, source.Asked);
    }

    [Fact]
    public async Task ReadAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeSystemThemeSource().ReadAsync(cts.Token));
    }
}
