using System.IO;
using System.Net.Http;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Tools;
using FlexDir.Core.Updates;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 설정 창 (docs/PRD-v2.md §12). 세 항목뿐이다 — 정보 · 시작 폴더 · 숨김 파일 보기.
/// <para>
/// <b>저장은 조작마다 즉시 한다.</b> 전역 뷰 상태(창을 닫을 때)보다 이르다 — 즐겨찾기와
/// 같은 판단이고, 강제 종료 한 번에 방금 정한 것이 날아가면 안 된다.
/// </para>
/// </summary>
public class SettingsViewModelTests
{
    private const string StateDirectory = @"C:\Users\tester\AppData\Roaming\flex-dir";

    private readonly FakeSettingsStore store = new();
    private readonly FakeUpdateSource updates = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly FakeSystemThemeSource systemTheme = new();
    private readonly FakeExternalToolCatalog catalog = new();
    private readonly FakeExternalToolLauncher launcher = new();

    private SettingsViewModel Create(
        string version = "0.3.1",
        IExternalToolCatalog? tools = null,
        IExternalToolLauncher? launcherPort = null) => new(
        store,
        dispatcher,
        version,
        StateDirectory,
        systemTheme,
        new UpdateViewModel(updates, dispatcher),
        tools,
        launcherPort);

    private static LocationId Loc(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    private async Task<SettingsViewModel> LoadedAsync(
        AppSettings? seed = null,
        IExternalToolCatalog? tools = null,
        IExternalToolLauncher? launcherPort = null)
    {
        if (seed is not null)
        {
            store.Seed(seed);
        }

        var settings = Create(tools: tools, launcherPort: launcherPort);
        await settings.LoadAsync(CancellationToken.None);

        return settings;
    }

    /// <summary>포트 둘이 배선된 설정 창. <c>[실행해 보기]</c> 를 보는 테스트가 쓴다.</summary>
    private Task<SettingsViewModel> LoadedWithToolsAsync(AppSettings? seed = null)
        => LoadedAsync(seed, catalog, launcher);

    // ── 정보 ──────────────────────────────────────────────────────

    [Fact]
    public void Version_IsWhateverTheHostHandsIn()
    {
        // 조립하는 쪽이 어셈블리에서 읽어 준다 (Host/Startup/ProductVersion). ViewModel 이
        // 진입 어셈블리를 직접 읽으면 테스트에서는 테스트 실행기의 버전이 나온다.
        Assert.Equal("0.3.1", Create("0.3.1").Version);
    }

    [Fact]
    public void StateFolderText_ShowsWhereTheStateLives()
    {
        Assert.Equal(StateDirectory, Create().StateFolderText);
    }

    [Fact]
    public void OpenStateFolder_AsksToNavigateThere()
    {
        // 우리가 파일 관리자다 — 상태 폴더도 활성 페인에서 연다. 탐색기를 띄우면 포트가
        // 하나 더 필요하고, 여기서 보고 싶은 것(settings.json·view-state.json)은 우리가
        // 이미 그릴 수 있는 것이다.
        var settings = Create();
        LocationId? asked = null;
        settings.NavigationRequested += (_, location) => asked = location;

        settings.OpenStateFolderCommand.Execute(null);

        Assert.Equal(Loc(StateDirectory), asked);
    }

    // ── 업데이트 수동 확인 ────────────────────────────────────────

    [Fact]
    public async Task CheckUpdate_WhenThereIsNothingNew_SaysSoOutLoud()
    {
        // 시작할 때의 자동 확인과 반대다 — 방금 누른 조작이라 아무 말도 하지 않으면
        // 눌렸는지조차 알 수 없다.
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.Checks);
        Assert.Contains("최신", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_WhenSomethingIsFound_NamesTheVersion()
    {
        updates.Available = new AvailableUpdate("0.4.0");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Contains("0.4.0", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_WhenTheFeedCannotBeReached_SaysItFailed()
    {
        updates.Failure = new HttpRequestException("피드에 못 닿는다");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Contains("확인하지 못했습니다", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_LeavesTheCheckingFlagDownWhenItIsOver()
    {
        // 버튼이 '확인 중…' 에 걸려 있으면 다시 누를 수 없다. 실패해도 내려와야 한다.
        updates.Failure = new HttpRequestException("피드에 못 닿는다");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.False(settings.IsCheckingUpdate);
    }

    [Fact]
    public async Task CheckUpdate_WithNoUpdateWiring_DoesNothingAndDoesNotThrow()
    {
        // 조립이 업데이트 없이 서는 경우가 있다 (WorkspaceViewModel 의 선택 인자와 같은 이유).
        var settings = new SettingsViewModel(store, dispatcher, "0.3.1", StateDirectory, systemTheme);

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, updates.Checks);
    }

    // ── 시작 폴더 ─────────────────────────────────────────────────

    [Fact]
    public async Task Load_BringsBackWhatWasSaved()
    {
        var settings = await LoadedAsync(new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Loc(@"C:\work"),
            ShowHiddenItems = true,
        });

        Assert.True(settings.StartsAtFixedFolder);
        Assert.False(settings.StartsAtLastFolder);
        Assert.Equal(@"C:\work", settings.StartFolderText);
        Assert.True(settings.ShowHiddenItems);
    }

    [Fact]
    public async Task Load_DoesNotWriteBackWhatItJustRead()
    {
        // 읽자마자 저장하면 앱을 켜기만 해도 파일이 다시 쓰인다 — 손으로 고쳐 둔 서식이
        // 매 실행마다 지워지고, 저장 실패가 시작 경로의 문제가 된다.
        await LoadedAsync(new AppSettings { ShowHiddenItems = true });

        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Load_WhenTheStoreThrows_LeavesTheDefaultsAndDoesNotThrow()
    {
        // 시작 경로가 이것을 지난다. 설정 하나 때문에 창이 안 뜨면 안 된다.
        store.LoadFailure = new IOException("파일이 잠겼다");
        var settings = Create();

        await settings.LoadAsync(CancellationToken.None);

        Assert.True(settings.StartsAtLastFolder);
        Assert.False(settings.ShowHiddenItems);
    }

    [Fact]
    public async Task ChoosingFixedMode_IsSavedImmediately()
    {
        var settings = await LoadedAsync();

        settings.StartsAtFixedFolder = true;
        await settings.SaveWork;

        Assert.Equal(StartFolderMode.Fixed, store.Current.StartMode);
    }

    [Fact]
    public async Task TypingAFolder_IsSavedImmediately()
    {
        var settings = await LoadedAsync();

        settings.StartFolderText = @"C:\work";
        await settings.SaveWork;

        Assert.Equal(Loc(@"C:\work"), store.Current.StartFolder);
        Assert.Null(settings.StartFolderError);
    }

    [Fact]
    public async Task TypingSomethingThatIsNotAPath_SaysWhyAndKeepsTheFolderUnset()
    {
        // 알아볼 수 없는 경로를 저장하면 다음 실행이 빈 페인으로 뜬다. 정하지 않은 것으로
        // 두면 시작 폴더 규칙이 마지막 폴더로 되돌아간다 (AppSettings.ResolveStartFolder).
        var settings = await LoadedAsync();

        settings.StartFolderText = "work";
        await settings.SaveWork;

        Assert.Equal("전체 경로를 입력하세요", settings.StartFolderError);
        Assert.Null(store.Current.StartFolder);
    }

    [Fact]
    public async Task ClearingTheFolder_IsNotAnError()
    {
        var settings = await LoadedAsync(new AppSettings { StartFolder = Loc(@"C:\work") });

        settings.StartFolderText = "   ";
        await settings.SaveWork;

        Assert.Null(settings.StartFolderError);
        Assert.Null(store.Current.StartFolder);
    }

    [Fact]
    public async Task UseCurrentFolder_FillsTheBoxAndSwitchesToFixedMode()
    {
        // 폴더를 채우기만 하고 모드를 그대로 두면 눌러도 아무 일이 없는 것처럼 보인다 —
        // 이 버튼을 누르는 이유가 '여기서 시작하겠다' 이기 때문이다.
        var settings = await LoadedAsync();

        settings.UseCurrentFolder(Loc(@"C:\work\project"));
        await settings.SaveWork;

        Assert.True(settings.StartsAtFixedFolder);
        Assert.Equal(@"C:\work\project", settings.StartFolderText);
        Assert.Equal(Loc(@"C:\work\project"), store.Current.StartFolder);
    }

    [Fact]
    public async Task UseCurrentFolder_WithNoFolderOpen_DoesNothing()
    {
        // 페인이 비어 있을 수 있다 (첫 실행에 폴백마저 없을 때).
        var settings = await LoadedAsync();

        settings.UseCurrentFolder(null);
        await settings.SaveWork;

        Assert.True(settings.StartsAtLastFolder);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Save_WhenTheStoreThrows_DoesNotThrowOutOfTheSetter()
    {
        // 세터는 바인딩이 부른다. 예외가 나가면 UI 스레드에서 잡을 사람이 없다.
        var settings = await LoadedAsync();
        store.SaveFailure = new IOException("디스크가 가득 찼다");

        settings.ShowHiddenItems = true;
        await settings.SaveWork;

        Assert.True(settings.ShowHiddenItems);   // 화면은 사용자가 고른 것을 그대로 둔다
    }

    // ── 숨김·시스템 파일 보기 ─────────────────────────────────────

    [Fact]
    public async Task ShowHiddenItems_IsSavedAndAnnounced()
    {
        // 목록과 트리가 지금 보고 있는 것을 곧바로 다시 걸러야 한다. 알리지 않으면 다음
        // 폴더로 옮길 때까지 화면이 옛 정책으로 남는다.
        var settings = await LoadedAsync();
        var announced = 0;
        settings.HiddenItemsChanged += (_, _) => announced++;

        settings.ShowHiddenItems = true;
        await settings.SaveWork;

        Assert.True(store.Current.ShowHiddenItems);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task ShowHiddenItems_SetToTheSameValue_ChangesNothing()
    {
        // 라디오·체크박스는 같은 값을 다시 밀어 넣는다. 그때마다 목록 둘과 트리를 다시
        // 읽으면 클릭 한 번에 저장소 호출이 쏟아진다.
        var settings = await LoadedAsync();
        var announced = 0;
        settings.HiddenItemsChanged += (_, _) => announced++;

        settings.ShowHiddenItems = false;
        await settings.SaveWork;

        Assert.Equal(0, announced);
        Assert.Equal(0, store.Saves);
    }

    // ── 터미널 선택 ───────────────────────────────────────────────

    [Fact]
    public async Task TerminalPreset_IsSavedAndAnnounced()
    {
        // 열려 있는 페인들이 다른 것을 열어야 하고, 탐지 대상도 바뀐다. 알리지 않으면
        // 새 탭을 만들 때까지 옛 프리셋으로 남는다 (WorkspaceViewModel 이 잇는다).
        var settings = await LoadedAsync();
        var announced = 0;
        settings.TerminalChanged += (_, _) => announced++;

        settings.TerminalPreset = TerminalPreset.GitBash;
        await settings.SaveWork;

        Assert.Equal(TerminalPreset.GitBash, store.Current.TerminalPreset);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task TerminalExecutableAndArguments_AreSavedAndAnnounced()
    {
        // 프리셋만 보면 사용자 지정 칸을 고치는 동안 아무 일도 일어나지 않는다 —
        // 셋 중 무엇이 바뀌어도 열려 있는 페인이 따라와야 한다.
        var settings = await LoadedAsync();
        var announced = 0;
        settings.TerminalChanged += (_, _) => announced++;

        settings.TerminalExecutable = @"C:\tools\alacritty.exe";
        await settings.SaveWork;

        Assert.Equal(1, announced);

        settings.TerminalArguments = "--working-directory \"" + ExternalToolCommand.PathToken + "\"";
        await settings.SaveWork;

        Assert.Equal(2, announced);
        Assert.Equal(@"C:\tools\alacritty.exe", store.Current.TerminalExecutable);
        Assert.Equal("--working-directory \"" + ExternalToolCommand.PathToken + "\"", store.Current.TerminalArguments);
    }

    [Fact]
    public async Task Terminal_SetToTheSameValue_ChangesNothing()
    {
        // 라디오는 같은 값을 다시 밀어 넣는다. 그때마다 알리면 설정 창을 여닫을 때마다
        // 페인 수만큼 레지스트리 조회가 나간다 (PaneViewModel.Terminal).
        var settings = await LoadedAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
            TerminalArguments = "-d",
        });
        var announced = 0;
        settings.TerminalChanged += (_, _) => announced++;

        settings.TerminalPreset = TerminalPreset.Custom;
        settings.TerminalExecutable = @"C:\tools\alacritty.exe";
        settings.TerminalArguments = "-d";
        await settings.SaveWork;

        Assert.Equal(0, announced);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task LoadAsync_AnnouncesTheTerminalItRead_Once()
    {
        // 저장 파일이 정한 터미널이 페인에 닿는 길은 이 알림뿐이다 — 세터들은 읽는 중에
        // 조용하다. 셋을 차례로 넣으므로 알림이 셋이 되면 중간 상태로 탐지가 헛돈다.
        store.Seed(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
            TerminalArguments = "-d",
        });

        var settings = Create();
        var announced = 0;
        settings.TerminalChanged += (_, _) => announced++;

        await settings.LoadAsync(CancellationToken.None);

        Assert.Equal(1, announced);
        Assert.Equal(
            new TerminalChoice(TerminalPreset.Custom, @"C:\tools\alacritty.exe", "-d"),
            settings.Current.ResolveTerminal());
    }

    [Fact]
    public async Task ResolveTerminal_MatchesWhatTheScreenShows()
    {
        // 페인에 실리는 것이 이 값이다 (WorkspaceViewModel). 화면과 어긋나면 사용자가 고른
        // 것과 다른 터미널이 뜬다.
        var settings = await LoadedAsync();

        settings.TerminalPreset = TerminalPreset.PowerShell7;

        Assert.Equal(new TerminalChoice(TerminalPreset.PowerShell7), settings.Current.ResolveTerminal());

        settings.TerminalExecutable = @"C:\tools\alacritty.exe";
        settings.TerminalArguments = "-d";

        // 아직 프리셋이라 사용자 지정 칸은 실리지 않는다 (AppSettings.ResolveTerminal).
        Assert.Equal(new TerminalChoice(TerminalPreset.PowerShell7), settings.Current.ResolveTerminal());

        settings.TerminalPreset = TerminalPreset.Custom;

        Assert.Equal(
            new TerminalChoice(TerminalPreset.Custom, @"C:\tools\alacritty.exe", "-d"),
            settings.Current.ResolveTerminal());
    }

    // ── 터미널 목록 (화면) ────────────────────────────────────────

    [Fact]
    public void TerminalOptions_AreTheSixPresets_InDeclarationOrder()
    {
        // 목록 순서가 곧 화면 순서다. 선언 순서에서 뽑아야 프리셋이 하나 늘 때 화면이
        // 저절로 따라온다 — 손으로 적어 두면 새 프리셋이 목록에서 조용히 빠진다.
        Assert.Equal(
            Enum.GetValues<TerminalPreset>(),
            Create().TerminalOptions.Select(option => option.Preset));
    }

    [Fact]
    public void TerminalOptions_ShowTheSharedLabels()
    {
        // 상태표시줄의 실패 문구와 같은 이름이어야 한다 (ExternalToolMessages.Describe).
        // 여기서 따로 적으면 목록에는 'Git Bash', 실패 문구에는 다른 이름이 뜬다.
        foreach (var option in Create().TerminalOptions)
        {
            Assert.Equal(ExternalToolCommand.Label(option.Preset), option.Label);
        }
    }

    [Fact]
    public async Task SelectedTerminal_MovesThePresetThatIsSaved()
    {
        var settings = await LoadedAsync();

        settings.SelectedTerminal = settings.TerminalOptions
            .Single(option => option.Preset == TerminalPreset.GitBash);
        await settings.SaveWork;

        Assert.Equal(TerminalPreset.GitBash, settings.Current.TerminalPreset);
        Assert.Equal(TerminalPreset.GitBash, store.Current.TerminalPreset);
    }

    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal, false)]
    [InlineData(TerminalPreset.CommandPrompt, false)]
    [InlineData(TerminalPreset.Custom, true)]
    public async Task IsCustomTerminal_IsTrueOnlyForCustom(TerminalPreset preset, bool expected)
    {
        var settings = await LoadedAsync();

        settings.SelectedTerminal = settings.TerminalOptions.Single(option => option.Preset == preset);

        Assert.Equal(expected, settings.IsCustomTerminal);
    }

    [Fact]
    public async Task SelectedTerminal_AnnouncesIsCustomTerminal()
    {
        // 아래 상자 둘의 IsEnabled 가 이 값이다. 알리지 않으면 '사용자 지정' 을 골라도
        // 칸이 잠긴 채로 남아, 고칠 수 있어야 할 것을 만질 수 없다.
        var settings = await LoadedAsync();
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.SelectedTerminal = settings.TerminalOptions
            .Single(option => option.Preset == TerminalPreset.Custom);

        Assert.Contains(nameof(SettingsViewModel.IsCustomTerminal), changed);
        Assert.Contains(nameof(SettingsViewModel.SelectedTerminal), changed);
    }

    [Fact]
    public async Task BlankCustomExecutable_IsStoredAsNothing()
    {
        // 지운 칸은 '아직 안 적었다' 이지 '빈 이름의 프로그램' 이 아니다. 빈 문자열이
        // 그대로 실리면 페인이 그것을 실행 파일로 알고 버튼을 켠다 (PaneViewModel).
        var settings = await LoadedAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
        });

        settings.TerminalExecutable = string.Empty;
        await settings.SaveWork;

        Assert.Null(settings.Current.TerminalExecutable);
        Assert.Null(store.Current.TerminalExecutable);
    }

    [Fact]
    public async Task LoadAsync_ShowsTheSavedCustomTerminal()
    {
        // 저장된 값이 화면 상태로 펴지지 않으면, 설정 창을 열 때마다 사용자 지정이
        // 'Windows Terminal' 로 보이고 아래 칸은 잠겨 있다.
        var settings = await LoadedAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
            TerminalArguments = "--working-directory \"" + ExternalToolCommand.PathToken + "\"",
        });

        Assert.Equal(TerminalPreset.Custom, settings.SelectedTerminal.Preset);
        Assert.True(settings.IsCustomTerminal);
        Assert.Equal(@"C:\tools\alacritty.exe", settings.TerminalExecutable);
        Assert.Equal(
            "--working-directory \"" + ExternalToolCommand.PathToken + "\"",
            settings.TerminalArguments);
    }

    // ── [실행해 보기] ─────────────────────────────────────────────

    [Fact]
    public async Task TestTerminal_WithAPreset_LaunchesWhatTheCatalogFound()
    {
        // 이 버튼의 존재 이유가 "이 프리셋이 이 기계에 있는가" 라서, 카탈로그를 지나지
        // 않으면 물어본 것이 아무것도 없다.
        catalog.Terminals[TerminalPreset.PowerShell7] = @"C:\Program Files\PowerShell\7\pwsh.exe";

        var settings = await LoadedWithToolsAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.PowerShell7,
        });

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Equal([TerminalPreset.PowerShell7], catalog.TerminalRequests);

        var launch = Assert.Single(launcher.Launches);

        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", launch.Executable);
        Assert.Equal(Loc(StateDirectory).DisplayPath, launch.Folder.DisplayPath);
    }

    [Fact]
    public async Task TestTerminal_PutsTheStateFolderIntoTheArguments()
    {
        // 치환하지 않으면 인자에 {path} 가 글자 그대로 넘어가고, 눌러 본 사람은 자기 틀이
        // 맞는지 여기서 알 수 없다. 시험 폴더는 상태 폴더다 — 설정 창은 페인을 모른다.
        catalog.Terminals[TerminalPreset.PowerShell7] = @"C:\Program Files\PowerShell\7\pwsh.exe";

        var settings = await LoadedWithToolsAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.PowerShell7,
        });

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Equal(
            $"-NoLogo -WorkingDirectory \"{Loc(StateDirectory).DisplayPath}\"",
            Assert.Single(launcher.Launches).Arguments);
    }

    [Fact]
    public async Task TestTerminal_WithCustom_UsesWhatWasTypedWithoutAsking()
    {
        // 사용자가 적은 경로는 찾아 주는 것이 아니라 받는 것이다 (IExternalToolCatalog 계약).
        var settings = await LoadedWithToolsAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
            TerminalArguments = "--working-directory \"" + ExternalToolCommand.PathToken + "\"",
        });

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Empty(catalog.TerminalRequests);

        var launch = Assert.Single(launcher.Launches);

        Assert.Equal(@"C:\tools\alacritty.exe", launch.Executable);
        Assert.Equal(
            $"--working-directory \"{Loc(StateDirectory).DisplayPath}\"",
            launch.Arguments);
    }

    [Fact]
    public async Task TestTerminal_WhenItIsNotOnThisMachine_SaysSoAndLaunchesNothing()
    {
        // 카탈로그에 넣어 두지 않았으니 이 기계에 없는 것이다.
        var settings = await LoadedWithToolsAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.GitBash,
        });

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Empty(launcher.Launches);
        Assert.Equal(
            ExternalToolMessages.Describe(LocationErrorKind.NotFound, "Git Bash"),
            settings.TerminalTestResult);
    }

    [Fact]
    public async Task TestTerminal_WhenTheLaunchIsRefused_SaysWhy()
    {
        catalog.Terminals[TerminalPreset.CommandPrompt] = @"C:\Windows\System32\cmd.exe";
        launcher.Result = LocationErrorKind.AccessDenied;

        var settings = await LoadedWithToolsAsync(new AppSettings
        {
            TerminalPreset = TerminalPreset.CommandPrompt,
        });

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Equal(
            ExternalToolMessages.Describe(LocationErrorKind.AccessDenied, "명령 프롬프트"),
            settings.TerminalTestResult);
    }

    [Fact]
    public async Task TestTerminal_WhenItOpens_SaysSo()
    {
        // 성공을 말하지 않는 페인과 다르다 (PaneViewModel.LaunchToolAsync). 여기서는
        // 사용자가 답을 보려고 누른 것이라, 아무 말이 없으면 눌리긴 한 건지 알 수 없다.
        catalog.Terminals[TerminalPreset.WindowsTerminal] = @"C:\Users\tester\wt.exe";

        var settings = await LoadedWithToolsAsync();

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Equal("Windows Terminal 을(를) 열었습니다", settings.TerminalTestResult);
    }

    [Fact]
    public async Task TestTerminal_ClearsTheOldResultBeforeTryingAgain()
    {
        // 옛 결과가 남아 있으면 방금 누른 것의 답으로 읽힌다 — 고친 뒤 다시 눌렀을 때
        // '찾을 수 없습니다' 가 그대로 떠 있으면 고친 것이 안 먹은 줄 안다.
        var gate = new GatedTerminalCatalog();
        var settings = await LoadedAsync(
            new AppSettings { TerminalPreset = TerminalPreset.PowerShell7 },
            gate,
            launcher);

        var first = settings.TestTerminalCommand.ExecuteAsync(null);
        gate.Answers[0].SetResult(null);
        await first;

        Assert.NotNull(settings.TerminalTestResult);

        var second = settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Null(settings.TerminalTestResult);

        gate.Answers[1].SetResult(null);
        await second;

        Assert.NotNull(settings.TerminalTestResult);
    }

    [Fact]
    public async Task TestTerminal_WithoutThePorts_DoesNothing()
    {
        // Update 와 같은 자리다 — 배선되지 않은 조립에서 버튼이 창을 죽이면 안 된다.
        var settings = await LoadedAsync();

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Null(settings.TerminalTestResult);
    }

    [Fact]
    public async Task TestTerminal_WhenTheLauncherThrows_TheCommandSurvives()
    {
        // 커맨드 밖으로 나간 예외는 잡을 사람이 없고 프로세스를 죽인다
        // (.harness/HANDOFF.md §규칙 10). 포트가 안 던지는 계약이어도 한 번 더 막는다.
        catalog.Terminals[TerminalPreset.WindowsTerminal] = @"C:\Users\tester\wt.exe";

        var settings = await LoadedAsync(seed: null, catalog, new ThrowingToolLauncher());

        await settings.TestTerminalCommand.ExecuteAsync(null);

        Assert.Equal(
            ExternalToolMessages.Describe(LocationErrorKind.Unknown, "Windows Terminal"),
            settings.TerminalTestResult);
    }

    /// <summary>계약(던지지 않는다)을 어긴 실행기. 새는 예외가 설정 창을 무너뜨리지 않는지 본다.</summary>
    private sealed class ThrowingToolLauncher : IExternalToolLauncher
    {
        public ValueTask<LocationErrorKind> LaunchAsync(
            string executable, string arguments, LocationId workingFolder, CancellationToken ct)
            => throw new InvalidOperationException("계약 위반");
    }

    /// <summary>
    /// 답을 테스트가 쥐고 있는 카탈로그. 조회가 매달려 있는 동안 화면이 어떤 상태인지
    /// 봐야 <see cref="SettingsViewModel.TerminalTestResult"/> 가 <b>언제</b> 지워지는지
    /// 채점할 수 있다.
    /// </summary>
    private sealed class GatedTerminalCatalog : IExternalToolCatalog
    {
        private readonly List<TaskCompletionSource<string?>> answers = [];

        /// <summary>조회 순서대로 쌓인다. 테스트가 원하는 순서로 푼다.</summary>
        public IReadOnlyList<TaskCompletionSource<string?>> Answers => answers;

        public ValueTask<string?> FindEditorAsync(CancellationToken ct)
            => ValueTask.FromResult<string?>(null);

        public ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct)
        {
            var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            answers.Add(answer);

            return new ValueTask<string?>(answer.Task);
        }
    }

    // ── 여닫기 ────────────────────────────────────────────────────

    [Fact]
    public void Panel_StartsClosed()
    {
        Assert.False(Create().IsOpen);
    }

    [Fact]
    public void OpenAndClose_FlipThePanel()
    {
        var settings = Create();

        settings.OpenCommand.Execute(null);
        Assert.True(settings.IsOpen);

        settings.CloseCommand.Execute(null);
        Assert.False(settings.IsOpen);
    }

    [Fact]
    public void OpeningAgain_ClearsTheLastUpdateMessage()
    {
        // '최신 버전입니다' 가 어제 누른 결과로 떠 있으면 방금 확인한 것처럼 읽힌다.
        var settings = Create();
        settings.OpenCommand.Execute(null);
        settings.CheckUpdateCommand.Execute(null);
        settings.CloseCommand.Execute(null);

        settings.OpenCommand.Execute(null);

        Assert.Equal(string.Empty, settings.UpdateStatus);
    }

    [Fact]
    public void OpenStateFolder_ClosesThePanel()
    {
        // 오버레이가 페인을 덮고 있다. 열어 놓고 그 폴더를 보여 주면 아무것도 안 보인다.
        var settings = Create();
        settings.OpenCommand.Execute(null);

        settings.OpenStateFolderCommand.Execute(null);

        Assert.False(settings.IsOpen);
    }

    // ── 현재 값 ───────────────────────────────────────────────────

    [Fact]
    public async Task Current_IsWhatTheStartFolderRuleWillSee()
    {
        // WorkspaceViewModel.RestoreAsync 가 이 값으로 페인을 연다.
        var settings = await LoadedAsync();

        settings.StartsAtFixedFolder = true;
        settings.StartFolderText = @"C:\work";
        await settings.SaveWork;

        Assert.Equal(Loc(@"C:\work"), settings.Current.ResolveStartFolder(Loc(@"C:\last"), null));
    }

    // ── 테마 (docs/PRD-v2.md §19) ────────────────────────────────

    [Fact]
    public void Default_FollowsTheSystemTheme()
    {
        var settings = Create();

        Assert.True(settings.IsSystemTheme);
        Assert.False(settings.IsLightTheme);
        Assert.False(settings.IsDarkTheme);
    }

    [Fact]
    public async Task InSystemMode_IsDarkModeFollowsTheOsValue()
    {
        systemTheme.IsDarkMode = true;
        var settings = await LoadedAsync();

        Assert.True(settings.IsDarkMode);
    }

    [Fact]
    public async Task ChoosingDarkTheme_IsSavedImmediatelyAndIgnoresTheOsValue()
    {
        systemTheme.IsDarkMode = false;   // OS 는 라이트인데도 다크를 고른다.
        var settings = await LoadedAsync();

        settings.IsDarkTheme = true;
        await settings.SaveWork;

        Assert.True(settings.IsDarkMode);
        Assert.Equal(ThemeMode.Dark, store.Current.Theme);
        Assert.False(settings.IsSystemTheme);
    }

    [Fact]
    public async Task ChoosingLightTheme_IsAlwaysLightRegardlessOfTheOs()
    {
        systemTheme.IsDarkMode = true;
        var settings = await LoadedAsync();

        settings.IsLightTheme = true;
        await settings.SaveWork;

        Assert.False(settings.IsDarkMode);
        Assert.Equal(ThemeMode.Light, store.Current.Theme);
    }

    [Fact]
    public async Task Load_BringsBackTheSavedTheme()
    {
        var settings = await LoadedAsync(new AppSettings { Theme = ThemeMode.Dark });

        Assert.True(settings.IsDarkTheme);
    }

    [Fact]
    public async Task RefreshSystemTheme_InSystemMode_PicksUpTheNewOsValue()
    {
        // WM_SETTINGCHANGE 를 받았을 때 SystemThemeWatcher 가 부르는 자리다.
        var settings = await LoadedAsync();
        Assert.False(settings.IsDarkMode);

        systemTheme.IsDarkMode = true;   // 사용자가 Windows 설정에서 다크로 바꿨다.
        var announced = 0;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsDarkMode))
            {
                announced++;
            }
        };

        await settings.RefreshSystemTheme(CancellationToken.None);

        Assert.True(settings.IsDarkMode);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task RefreshSystemTheme_InDarkMode_IgnoresTheOsValue()
    {
        // 사용자가 고정으로 고른 것을 OS 재조회가 뒤집으면 안 된다.
        var settings = await LoadedAsync();
        settings.IsDarkTheme = true;
        await settings.SaveWork;

        systemTheme.IsDarkMode = false;
        await settings.RefreshSystemTheme(CancellationToken.None);

        Assert.True(settings.IsDarkMode);
    }
}
