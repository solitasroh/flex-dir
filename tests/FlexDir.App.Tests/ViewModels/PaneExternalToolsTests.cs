using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 툴바의 외부 도구 버튼 둘 — <c>[VS]</c> 와 <c>[&gt;_]</c> (docs/PRD-v2.md §19).
/// <para>
/// 두 버튼은 <b>언제나 현재 폴더</b>를 연다 — 선택 항목은 보지 않는다. 여기서 재는 것은
/// <b>판정과 배선</b>이다: 언제 눌리는가(탐지 · 서버 루트), 무엇을 어떤 인자로 넘기는가,
/// 실패를 어떻게 말하는가. 실제 프로세스는 <see cref="FakeExternalToolLauncher"/> 가
/// 대신하므로 이 테스트로 창이 뜨지 않는다.
/// </para>
/// </summary>
public class PaneExternalToolsTests
{
    private const string EditorPath = @"C:\Program Files\Microsoft VS Code\Code.exe";

    private const string TerminalPath = @"C:\Users\Me\AppData\Local\Microsoft\WindowsApps\wt.exe";

    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly FakeExternalToolCatalog catalog = new();
    private readonly FakeExternalToolLauncher launcher = new();

    [Fact]
    public async Task WithoutThePorts_BothButtonsAreOff_AndThePaneStillWorks()
    {
        // 포트는 선택 주입이다 (driveSpace·knownFolders 와 같은 자리). 주지 않은 기존 페인
        // 테스트 수백 개가 그대로 돌아야 한다.
        var pane = new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

        await pane.NavigateAsync(Folder(@"C:\Temp", "a.txt"));
        pane.RefreshExternalTools();
        await pane.ExternalToolsWork;

        Assert.False(pane.CanOpenInEditor);
        Assert.False(pane.CanOpenInTerminal);
        Assert.Single(pane.Items);
        Assert.Equal(PaneStatus.Idle, pane.Status);
    }

    [Fact]
    public async Task WhenTheEditorIsInstalled_ItsButtonTurnsOn()
    {
        catalog.Editor = EditorPath;
        var pane = await OpenedPane(@"C:\Temp");

        Assert.True(pane.CanOpenInEditor);
    }

    [Fact]
    public async Task WhenTheEditorIsMissing_ItsButtonStaysOff()
    {
        // 사람 확인 8번의 기계 판정 — VS Code 를 지우면 버튼이 회색이어야 한다.
        catalog.Editor = null;
        catalog.Terminals[TerminalPreset.WindowsTerminal] = TerminalPath;
        var pane = await OpenedPane(@"C:\Temp");

        Assert.False(pane.CanOpenInEditor);

        // 터미널은 그대로다 — 하나가 없다고 둘 다 죽지 않는다.
        Assert.True(pane.CanOpenInTerminal);
    }

    [Fact]
    public async Task OnAServerRoot_BothButtonsAreOff()
    {
        // \\server 에 있는 것은 폴더가 아니라 공유다. 작업 디렉터리로 세울 수 없다.
        Equip();
        var pane = await OpenedPane(@"\\10.10.10.23");

        Assert.False(pane.CanOpenInEditor);
        Assert.False(pane.CanOpenInTerminal);
    }

    [Fact]
    public async Task BelowAShare_BothButtonsAreOn()
    {
        // UNC 아래는 정상적인 작업 디렉터리다 — IsNetwork 로 막으면 NAS 에서 영영 죽는다.
        Equip();
        var pane = await OpenedPane(@"\\10.10.10.23\home");

        Assert.True(pane.CanOpenInEditor);
        Assert.True(pane.CanOpenInTerminal);
    }

    [Fact]
    public async Task MovingFromTheServerRootToAShare_FlipsTheJudgment_AndAnnouncesIt()
    {
        // 알리지 않으면 공유로 내려가도 버튼이 회색인 채로 남는다 (WPF 바인딩은 런타임 조회다).
        Equip();
        var pane = await OpenedPane(@"\\10.10.10.23");
        var changed = new List<string?>();
        pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await pane.NavigateAsync(Folder(@"\\10.10.10.23\home", "a.txt"));

        Assert.True(pane.CanOpenInEditor);
        Assert.True(pane.CanOpenInTerminal);
        Assert.Contains(nameof(PaneViewModel.CanOpenInEditor), changed);
        Assert.Contains(nameof(PaneViewModel.CanOpenInTerminal), changed);
    }

    [Fact]
    public async Task TheEditorCommand_OpensTheCurrentFolder_QuotedOnce()
    {
        // 따옴표를 여기서 또 붙이면 ""C:\Temp"" 가 되어 VS Code 가 빈 창을 연다.
        catalog.Editor = EditorPath;
        var pane = await OpenedPane(@"C:\Temp");

        await pane.OpenInEditorCommand.ExecuteAsync(null);

        var launch = Assert.Single(launcher.Launches);
        Assert.Equal(EditorPath, launch.Executable);
        Assert.Equal("\"C:\\Temp\"", launch.Arguments);
        Assert.Equal(@"C:\Temp", launch.Folder.DisplayPath);
    }

    [Fact]
    public async Task TheTerminalCommand_FillsThePresetTemplate()
    {
        catalog.Terminals[TerminalPreset.WindowsTerminal] = TerminalPath;
        var pane = await OpenedPane(@"C:\Temp");

        await pane.OpenInTerminalCommand.ExecuteAsync(null);

        var launch = Assert.Single(launcher.Launches);
        Assert.Equal(TerminalPath, launch.Executable);
        Assert.Equal("-d \"C:\\Temp\"", launch.Arguments);
    }

    [Fact]
    public async Task Refreshing_AsksOnlyTheChosenPreset()
    {
        // 화면에 쓰이지 않는 넷의 레지스트리 조회가 창을 열 때마다 나가면 안 된다.
        var pane = await OpenedPane(@"C:\Temp");

        Assert.Equal([TerminalPreset.WindowsTerminal], catalog.TerminalRequests);
        Assert.NotNull(pane);
    }

    [Fact]
    public async Task ACustomTerminal_NeedsNoDetection()
    {
        // 사람 확인 6번의 기계 판정. 사용자가 적은 경로는 찾아 주는 것이 아니라 받는 것이다 —
        // 카탈로그는 Custom 에 언제나 null 을 낸다 (IExternalToolCatalog 계약 4).
        var pane = await OpenedPane(@"C:\Temp");
        pane.Terminal = new TerminalChoice(TerminalPreset.Custom, "wezterm-gui.exe", "start --cwd \"{path}\"");
        await pane.ExternalToolsWork;

        Assert.True(pane.CanOpenInTerminal);

        await pane.OpenInTerminalCommand.ExecuteAsync(null);

        var launch = Assert.Single(launcher.Launches);
        Assert.Equal("wezterm-gui.exe", launch.Executable);
        Assert.Equal("start --cwd \"C:\\Temp\"", launch.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACustomTerminalWithoutAnExecutable_StaysOff(string? executable)
    {
        // 설정 창에서 Custom 을 고르고 아직 경로를 안 적은 중간 상태가 정상이다.
        var pane = await OpenedPane(@"C:\Temp");
        pane.Terminal = new TerminalChoice(TerminalPreset.Custom, executable, "--cwd \"{path}\"");
        await pane.ExternalToolsWork;

        Assert.False(pane.CanOpenInTerminal);
    }

    [Fact]
    public async Task WhenTheLaunchFails_TheStatusLineNamesTheTool()
    {
        catalog.Editor = EditorPath;
        launcher.Result = LocationErrorKind.NotFound;
        var pane = await OpenedPane(@"C:\Temp");

        await pane.OpenInEditorCommand.ExecuteAsync(null);

        // 무엇을 열려다 실패했는지가 없으면 사용자는 폴더가 사라진 줄 안다.
        Assert.Contains("VS Code", pane.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheTerminalLaunchFails_TheStatusLineNamesThePreset()
    {
        catalog.Terminals[TerminalPreset.WindowsTerminal] = TerminalPath;
        launcher.Result = LocationErrorKind.AccessDenied;
        var pane = await OpenedPane(@"C:\Temp");

        await pane.OpenInTerminalCommand.ExecuteAsync(null);

        Assert.Contains("Windows Terminal", pane.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheLaunchSucceeds_TheStatusLineIsLeftAlone()
    {
        // 말할 것이 있을 때만 말한다 — 성공을 알리면 항목 수 요약이 이유 없이 지워진다.
        catalog.Editor = EditorPath;
        var pane = await OpenedPane(@"C:\Temp");
        var before = pane.StatusText;

        await pane.OpenInEditorCommand.ExecuteAsync(null);

        Assert.Equal(before, pane.StatusText);
        Assert.NotEqual(string.Empty, pane.StatusText);
    }

    [Fact]
    public async Task WhenTheLauncherThrows_ThePaneSurvives()
    {
        // 커맨드 밖으로 나간 예외는 잡을 사람이 없고 프로세스를 죽인다
        // (.harness/HANDOFF.md §규칙 10 — 실물에서 밟았다).
        catalog.Editor = EditorPath;
        var pane = await OpenedPane(@"C:\Temp", launcherPort: new ThrowingToolLauncher());

        await pane.OpenInEditorCommand.ExecuteAsync(null);

        Assert.Single(pane.Items);
        Assert.Contains("VS Code", pane.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshingTwice_AsksTheCatalogTwice()
    {
        // 상주 앱(ADR-003)이라 창이 다시 보일 때마다 다시 묻는다. 캐시하면 지운 뒤에도
        // 버튼이 살아 있다.
        var pane = await OpenedPane(@"C:\Temp");

        pane.RefreshExternalTools();
        await pane.ExternalToolsWork;

        Assert.Equal(2, catalog.EditorRequests);
        Assert.Equal(2, catalog.TerminalRequests.Count);
    }

    [Fact]
    public async Task WhenTheToolAppears_TheButtonAndItsCommandBothAnnounce()
    {
        var pane = await OpenedPane(@"C:\Temp");
        Assert.False(pane.CanOpenInEditor);
        var changed = new List<string?>();
        var canExecuteChanged = 0;
        pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        pane.OpenInEditorCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        // 창이 숨어 있는 동안 사용자가 VS Code 를 설치했다.
        catalog.Editor = EditorPath;
        pane.RefreshExternalTools();
        await pane.ExternalToolsWork;

        Assert.True(pane.CanOpenInEditor);
        Assert.Contains(nameof(PaneViewModel.CanOpenInEditor), changed);

        // [RelayCommand] 는 스스로 감지하지 않는다. 안 내면 버튼이 영원히 회색이다.
        Assert.True(canExecuteChanged > 0);
    }

    [Fact]
    public async Task ChangingTheTerminal_ReDetects_AndTheSameValueDoesNot()
    {
        var pane = await OpenedPane(@"C:\Temp");
        Assert.Equal([TerminalPreset.WindowsTerminal], catalog.TerminalRequests);

        pane.Terminal = new TerminalChoice(TerminalPreset.PowerShell7);
        await pane.ExternalToolsWork;

        Assert.Equal([TerminalPreset.WindowsTerminal, TerminalPreset.PowerShell7], catalog.TerminalRequests);

        // 같은 값을 다시 미는 것이 재탐지 폭풍이 되면 안 된다 (설정 적용은 값을 통째로 민다).
        pane.Terminal = new TerminalChoice(TerminalPreset.PowerShell7);
        await pane.ExternalToolsWork;

        Assert.Equal([TerminalPreset.WindowsTerminal, TerminalPreset.PowerShell7], catalog.TerminalRequests);
    }

    [Fact]
    public async Task WhenTwoRefreshesOverlap_TheLastOneWins()
    {
        // 창을 빠르게 여닫으면 조회가 실제로 겹친다. 먼저 시작한 느린 조회가 나중 답을
        // 덮으면 화면이 과거로 되돌아간다.
        var gated = new GatedToolCatalog();
        var pane = new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus,
            externalTools: gated, toolLauncher: launcher);
        await pane.NavigateAsync(Folder(@"C:\Temp", "a.txt"));

        pane.RefreshExternalTools();
        var first = pane.ExternalToolsWork;
        pane.RefreshExternalTools();
        var second = pane.ExternalToolsWork;

        // 나중 것이 먼저 답하고, 앞선 것이 뒤늦게 도착한다.
        gated.Answers[1].SetResult(@"C:\new\Code.exe");
        await second;
        gated.Answers[0].SetResult(@"C:\old\Code.exe");
        await first;

        await pane.OpenInEditorCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\new\Code.exe", Assert.Single(launcher.Launches).Executable);
    }

    /// <summary>탐지가 끝난 페인. 폴더를 열고 한 번 물은 상태다.</summary>
    private async Task<PaneViewModel> OpenedPane(string path, IExternalToolLauncher? launcherPort = null)
    {
        var pane = CreatePane(launcherPort);

        await pane.NavigateAsync(Folder(path, "a.txt"));
        pane.RefreshExternalTools();
        await pane.ExternalToolsWork;

        return pane;
    }

    private PaneViewModel CreatePane(IExternalToolLauncher? launcherPort = null)
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus,
            externalTools: catalog, toolLauncher: launcherPort ?? launcher);

    /// <summary>둘 다 설치된 기계.</summary>
    private void Equip()
    {
        catalog.Editor = EditorPath;
        catalog.Terminals[TerminalPreset.WindowsTerminal] = TerminalPath;
    }

    private LocationId Folder(string path, string name)
    {
        var folder = Location(path);

        source.Folders[folder] = [new FileItem(
            name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None)];

        return folder;
    }

    private static LocationId Location(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }

    /// <summary>계약(던지지 않는다)을 어긴 실행기. 새는 예외가 페인을 무너뜨리지 않는지 본다.</summary>
    private sealed class ThrowingToolLauncher : IExternalToolLauncher
    {
        public ValueTask<LocationErrorKind> LaunchAsync(
            string executable, string arguments, LocationId workingFolder, CancellationToken ct)
            => throw new InvalidOperationException("계약 위반");
    }

    /// <summary>
    /// 답을 테스트가 쥐고 있는 카탈로그. <b>취소를 일부러 무시한다</b> — 겹친 조회에서
    /// 나중 것이 이기는 판정이 취소가 아니라 <b>도착 시점의 가드</b>에서 나오는지 보려면
    /// 앞선 조회도 끝까지 답해야 한다.
    /// </summary>
    private sealed class GatedToolCatalog : IExternalToolCatalog
    {
        private readonly List<TaskCompletionSource<string?>> answers = [];

        /// <summary>조회 순서대로 쌓인다. 테스트가 원하는 순서로 푼다.</summary>
        public IReadOnlyList<TaskCompletionSource<string?>> Answers => answers;

        public ValueTask<string?> FindEditorAsync(CancellationToken ct)
        {
            var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            answers.Add(answer);

            return new ValueTask<string?>(answer.Task);
        }

        public ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct)
            => ValueTask.FromResult<string?>(null);
    }
}
