using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.Core.Tests.Settings;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시키고, <see cref="AppSettings"/> 의 규칙을 함께 고정한다.
/// 실제 구현체(<c>JsonSettingsStore</c>)는 <c>FlexDir.Shell</c> 의 몫이다.
/// <para>
/// <b>뷰 상태와 다른 저장소인 이유</b>: 설정은 캐시가 아니라 <b>사용자의 의도</b>다.
/// <c>IViewStateStore</c> 는 읽기 실패를 조용히 기본값으로 접는데 (CLAUDE.md §4 — 어긋나면
/// 파일시스템을 믿는다), 설정에 그렇게 하면 "숨김 파일을 보겠다" 고 정해 둔 것이 조용히
/// 뒤집힌다. 즐겨찾기(<c>IFavoriteStore</c>)와 같은 자리다.
/// </para>
/// </summary>
public class FakeSettingsStoreTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task LoadAsync_BeforeAnythingIsSaved_IsDefault()
    {
        // 처음 켠 사람에게는 설정이 없다. 오류가 아니다.
        Assert.Equal(AppSettings.Default, await new FakeSettingsStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public void Default_StartsAtTheLastFolderAndHidesHiddenItems()
    {
        // 탐색기의 기본값과 같다. 숨김을 기본으로 켜면 C:\ 가 $Recycle.Bin 부터 시작한다.
        Assert.Equal(StartFolderMode.LastFolder, AppSettings.Default.StartMode);
        Assert.Null(AppSettings.Default.StartFolder);
        Assert.False(AppSettings.Default.ShowHiddenItems);
    }

    [Fact]
    public void Default_FollowsTheSystemTheme()
    {
        // 처음 켠 사람은 OS 를 따라간다 — 다크로 쓰던 사람 앞에 라이트 창이 뜨면 안 된다.
        Assert.Equal(ThemeMode.System, AppSettings.Default.Theme);
    }

    [Fact]
    public void Default_OpensWindowsTerminal()
    {
        // Windows 11 의 기본 터미널이다. 없는 기계에서는 탐지가 null 을 내고 버튼이 비활성이
        // 되며, 그때 설정에서 다른 것을 고르면 된다 — 기본값이 틀렸다고 앱이 막히지 않는다.
        Assert.Equal(TerminalPreset.WindowsTerminal, AppSettings.Default.TerminalPreset);
        Assert.Null(AppSettings.Default.TerminalExecutable);
        Assert.Null(AppSettings.Default.TerminalArguments);
    }

    [Fact]
    public async Task SavedSettings_AreLoadedBack()
    {
        var store = new FakeSettingsStore();
        var settings = new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Path(@"C:\work"),
            ShowHiddenItems = true,
            Theme = ThemeMode.Dark,
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\wezterm-gui.exe",
            TerminalArguments = "start --cwd \"{path}\"",
        };

        await store.SaveAsync(settings, CancellationToken.None);

        Assert.Equal(settings, await store.LoadAsync(CancellationToken.None));
    }

    // ── 터미널 선택 (docs/PRD-v2.md §20) ─────────────────────────
    //
    // 프리셋과 사용자 지정 두 항목을 하나의 TerminalChoice 로 접는다. 실행하는 쪽과 실행
    // 파일을 탐지하는 쪽이 각자 접으면 조용히 다른 답을 낸다 — ResolveStartFolder 와 같은
    // 이유로 규칙이 Core 에 있다.

    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal)]
    [InlineData(TerminalPreset.PowerShell7)]
    [InlineData(TerminalPreset.WindowsPowerShell)]
    [InlineData(TerminalPreset.CommandPrompt)]
    [InlineData(TerminalPreset.GitBash)]
    public void ResolveTerminal_WithAPreset_CarriesThePresetAlone(TerminalPreset preset)
    {
        // 프리셋의 실행 파일·인자는 ExternalToolCommand.Resolve 의 표에서 나온다. 여기서
        // 사용자 지정 칸을 함께 실으면 Custom 을 써 보고 되돌린 사람의 옛 값이 프리셋 인자로
        // 새어 나간다.
        var settings = new AppSettings
        {
            TerminalPreset = preset,
            TerminalExecutable = @"C:\tools\wezterm-gui.exe",
            TerminalArguments = "start --cwd \"{path}\"",
        };

        Assert.Equal(new TerminalChoice(preset), settings.ResolveTerminal());
    }

    [Fact]
    public void ResolveTerminal_WithCustom_CarriesTheExecutableAndArguments()
    {
        var settings = new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\wezterm-gui.exe",
            TerminalArguments = "start --cwd \"{path}\"",
        };

        Assert.Equal(
            new TerminalChoice(TerminalPreset.Custom, @"C:\tools\wezterm-gui.exe", "start --cwd \"{path}\""),
            settings.ResolveTerminal());
    }

    [Fact]
    public void ResolveTerminal_WithCustomAndNothingTyped_DoesNotThrow()
    {
        // 사용자 지정을 고르고 아직 경로를 안 적은 중간 상태다 (StartFolder 와 같은 자리).
        // 던지면 설정 창이 뜨는 도중에 죽는다 — 버튼이 비활성일 뿐이다.
        var settings = new AppSettings { TerminalPreset = TerminalPreset.Custom };

        Assert.Equal(new TerminalChoice(TerminalPreset.Custom, null, null), settings.ResolveTerminal());
    }

    // ── 다크모드 판정 ───────────────────────────────────────────────
    //
    // 실제 화면(브러시)에 닿지 않는 순수 규칙이다 — App 의 SettingsViewModel 과 Host 양쪽이
    // 같은 답을 내야 하므로 여기 Core 에 둔다 (ResolveStartFolder 와 같은 이유).

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveIsDarkMode_InSystemMode_TakesTheSystemValue(bool systemIsDark)
    {
        var settings = new AppSettings { Theme = ThemeMode.System };

        Assert.Equal(systemIsDark, settings.ResolveIsDarkMode(systemIsDark));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveIsDarkMode_InLightMode_IsAlwaysLight(bool systemIsDark)
    {
        // 사용자가 라이트를 골랐으면 OS 가 다크여도 라이트다 — 그것이 이 모드를 고르는 이유다.
        var settings = new AppSettings { Theme = ThemeMode.Light };

        Assert.False(settings.ResolveIsDarkMode(systemIsDark));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveIsDarkMode_InDarkMode_IsAlwaysDark(bool systemIsDark)
    {
        var settings = new AppSettings { Theme = ThemeMode.Dark };

        Assert.True(settings.ResolveIsDarkMode(systemIsDark));
    }

    // ── 시작 폴더 규칙 ──────────────────────────────────────────────
    //
    // 페인마다 한 번씩 적용한다. 규칙이 여기 있는 이유는 Host 와 ViewModel 두 곳이 같은
    // 답을 내야 하기 때문이다 — 어느 한쪽에 두면 다른 쪽이 조용히 다르게 판정한다.

    [Fact]
    public void ResolveStartFolder_InLastFolderMode_TakesTheRememberedFolder()
    {
        Assert.Equal(
            Path(@"C:\last"),
            AppSettings.Default.ResolveStartFolder(Path(@"C:\last"), Path(@"C:\home")));
    }

    [Fact]
    public void ResolveStartFolder_WithNothingRemembered_TakesTheFallback()
    {
        // 첫 실행이다. 빈 페인으로 시작하면 매번 주소를 쳐야 한다.
        Assert.Equal(Path(@"C:\home"), AppSettings.Default.ResolveStartFolder(null, Path(@"C:\home")));
    }

    [Fact]
    public void ResolveStartFolder_WithNeither_IsNull()
    {
        // 폴백마저 없으면 비워 둔다. 아무 폴더나 고르지 않는다.
        Assert.Null(AppSettings.Default.ResolveStartFolder(null, null));
    }

    [Fact]
    public void ResolveStartFolder_InFixedMode_IgnoresTheRememberedFolder()
    {
        // '정해 둔 폴더' 는 마지막 폴더를 이긴다 — 그것이 이 모드를 고르는 이유다.
        var settings = new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Path(@"C:\work"),
        };

        Assert.Equal(Path(@"C:\work"), settings.ResolveStartFolder(Path(@"C:\last"), Path(@"C:\home")));
    }

    [Fact]
    public void ResolveStartFolder_InFixedModeWithNoFolderChosen_FallsBackToTheLastFolder()
    {
        // 모드만 고르고 폴더를 아직 정하지 않은 상태다. 그때 빈 페인을 내면 설정을 만진 것이
        // 앱을 망가뜨린 것처럼 보인다 — 정하기 전까지는 원래대로 연다.
        var settings = new AppSettings { StartMode = StartFolderMode.Fixed };

        Assert.Equal(Path(@"C:\last"), settings.ResolveStartFolder(Path(@"C:\last"), Path(@"C:\home")));
    }

    [Fact]
    public void ResolveStartFolder_DoesNotAskWhetherTheFolderStillExists()
    {
        // Core 는 파일시스템에 닿지 않는다. 사라진 폴더는 페인이 상위로 올라가며 처리한다
        // (docs/PRD.md §4) — 여기서 존재를 확인하면 시작 경로가 저장소 호출을 하나 더 낸다.
        var settings = new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Path(@"C:\gone-yesterday"),
        };

        Assert.Equal(Path(@"C:\gone-yesterday"), settings.ResolveStartFolder(null, Path(@"C:\home")));
    }
}
