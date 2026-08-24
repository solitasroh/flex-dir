using System.Diagnostics;
using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Settings;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 외부 도구가 창 전체에 닿는 자리 (docs/PRD-v2.md §19~20).
/// <para>
/// <b>워크스페이스가 잇는다.</b> 페인은 설정을 모르고 (<c>PaneViewModel.Terminal</c>), 설정은
/// 페인을 모른다 (<c>SettingsViewModel</c>) — 숨김 정책과 완전히 같은 구도다. 여기서 재는
/// 것은 그 배선 둘이다: <b>설정이 바뀌면 열려 있는 모든 곳에 닿는가</b>, 그리고 <b>창이 다시
/// 보일 때 다시 찾는가</b> (ADR-003 상주 프로세스 — 그 사이에 사용자가 VS Code 를 설치하거나
/// 지웠을 수 있다).
/// </para>
/// </summary>
public class WorkspaceExternalToolsTests
{
    private const string StateDirectory = @"C:\Users\tester\AppData\Roaming\flex-dir";

    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new InMemoryViewStateStore().RememberingTwoPanes();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly FakeSettingsStore settingsStore = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly FakeExternalToolCatalog catalog = new();
    private readonly FakeExternalToolLauncher launcher = new();

    // ── 설정 → 페인 ───────────────────────────────────────────────

    [Fact]
    public async Task Restore_PutsTheChosenTerminalInEveryPane()
    {
        settingsStore.Seed(new AppSettings { TerminalPreset = TerminalPreset.GitBash });

        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(2, workspace.Panes.Count);
        Assert.All(workspace.AllPanes, pane => Assert.Equal(Choice(TerminalPreset.GitBash), pane.Terminal));

        // 탭까지 내려가야 툴바 버튼이 그것을 연다 — 페인이 값을 들고만 있으면 소용이 없다.
        Assert.All(workspace.AllPanes, pane => Assert.Equal(Choice(TerminalPreset.GitBash), pane.Active.Terminal));
    }

    [Fact]
    public async Task Restore_WithACustomTerminal_CarriesTheExecutableAndTheArguments()
    {
        var arguments = "--working-directory \"" + ExternalToolCommand.PathToken + "\"";

        settingsStore.Seed(new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = @"C:\tools\alacritty.exe",
            TerminalArguments = arguments,
        });

        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var expected = new TerminalChoice(TerminalPreset.Custom, @"C:\tools\alacritty.exe", arguments);

        Assert.All(workspace.AllPanes, pane => Assert.Equal(expected, pane.Active.Terminal));
    }

    [Fact]
    public async Task Restore_PushesTheTerminalItself_WithoutLeaningOnTheAnnouncement()
    {
        // 복원은 알림에 기대지 않는다. 이미 읽어 둔 설정을 다시 읽으면 달라진 것이 없어
        // 조용하고 (SettingsViewModel.LoadAsync), 그때도 페인은 설정이 정한 것으로 서야 한다.
        settingsStore.Seed(new AppSettings { TerminalPreset = TerminalPreset.GitBash });

        var (workspace, settings) = Create();
        await settings.LoadAsync(CancellationToken.None);

        // 어긋난 페인을 만들어 둔다 — 복원이 스스로 밀지 않으면 이대로 남는다.
        workspace.Panes[0].Terminal = Choice(TerminalPreset.CommandPrompt);

        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.All(workspace.AllPanes, pane => Assert.Equal(Choice(TerminalPreset.GitBash), pane.Terminal));
    }

    [Fact]
    public async Task ANewTab_StartsWithTheTerminalThePaneIsAlreadyUsing()
    {
        // 페인이 소유자다 (ShowHiddenItems 와 같은 자리) — 아니면 나중에 만든 탭만 기본
        // 프리셋으로 뜨고 아무도 다시 밀지 않는다.
        settingsStore.Seed(new AppSettings { TerminalPreset = TerminalPreset.PowerShell7 });

        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var tab = workspace.LeftTabs().NewTab();

        Assert.Equal(Choice(TerminalPreset.PowerShell7), tab.Terminal);
    }

    [Fact]
    public async Task APaneBornFromSplitting_StartsWithTheSameTerminal()
    {
        // 분할은 복원 뒤에 늘어난다. 복제하지 않으면 3·4번 페인만 기본 프리셋으로 남는다.
        settingsStore.Seed(new AppSettings { TerminalPreset = TerminalPreset.CommandPrompt });

        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        workspace.SetSplitCommand.Execute(4);
        await workspace.SplitWork;

        Assert.Equal(4, workspace.Panes.Count);
        Assert.All(workspace.Panes, pane => Assert.Equal(Choice(TerminalPreset.CommandPrompt), pane.Active.Terminal));
    }

    [Fact]
    public async Task WhenTheSettingChanges_EveryTabOfEveryPaneFollows()
    {
        var (workspace, settings) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (background, folded) = await SpreadOutAsync(workspace);

        settings.TerminalPreset = TerminalPreset.GitBash;

        foreach (var pane in workspace.AllPanes)
        {
            foreach (var tab in pane.Tabs)
            {
                Assert.Equal(Choice(TerminalPreset.GitBash), tab.Terminal);
            }
        }

        // 배경 탭과 접힌 페인이 이 테스트의 전부다 — ApplyHiddenItemsAsync 가 도는 범위와
        // 같아야 한다. 보이는 활성 탭만 밀면 탭을 바꾸는 순간 옛 프리셋이 돌아온다.
        Assert.Equal(Choice(TerminalPreset.GitBash), background.Terminal);
        Assert.Equal(Choice(TerminalPreset.GitBash), folded.Active.Terminal);
    }

    [Fact]
    public async Task WithNoSettingsWired_TheTerminalPlumbingDoesNotThrow()
    {
        // 설정 없이 서는 조립이 있다 (트리·업데이트와 같은 이유로 선택 인자다).
        var workspace = new WorkspaceViewModel(CreatePane, viewStates);
        await workspace.RestoreAsync(null, CancellationToken.None);

        var detached = new SettingsViewModel(
            settingsStore, dispatcher, "0.3.1", StateDirectory, new FakeSystemThemeSource());

        detached.TerminalPreset = TerminalPreset.GitBash;
        workspace.OnWindowShown();

        Assert.All(workspace.AllPanes, pane => Assert.Equal(Choice(TerminalPreset.WindowsTerminal), pane.Terminal));
    }

    // ── 창이 다시 보임 → 재탐지 ────────────────────────────────────

    [Fact]
    public async Task OnWindowShown_AsksTheCatalogAgainForEveryTab()
    {
        // 사람 확인 8번의 기계 판정 — 창을 숨긴 사이에 VS Code 를 지웠으면 다시 보일 때
        // 버튼이 꺼져야 하고, 그러려면 다시 물어야 한다 (포트는 캐시하지 않는다).
        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await SpreadOutAsync(workspace);

        var tabs = workspace.AllPanes.Sum(pane => pane.Tabs.Count);
        var before = catalog.EditorRequests;

        workspace.OnWindowShown();

        Assert.Equal(4, tabs);                                  // 페인 셋 · 그중 하나가 탭 둘
        Assert.Equal(before + tabs, catalog.EditorRequests);
    }

    [Fact]
    public async Task OnWindowShown_Twice_AsksTwice()
    {
        // 창은 여러 번 다시 보인다 (ADR-003). 두 번째에 조용하면 첫 표시 뒤의 설치·삭제가
        // 영영 반영되지 않는다.
        var (workspace, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var tabs = workspace.AllPanes.Sum(pane => pane.Tabs.Count);
        var before = catalog.EditorRequests;

        workspace.OnWindowShown();
        workspace.OnWindowShown();

        Assert.Equal(before + (tabs * 2), catalog.EditorRequests);
    }

    [Fact]
    public async Task OnWindowShown_DoesNotWaitForTheLookup()
    {
        // 창이 뜨는 길이다. WindowShown 예산은 100ms 이고 레지스트리 조회는 그것을 통째로
        // 먹는다 (.harness/HANDOFF.md §Host 가 지금 하는 일).
        var gated = new GatedToolCatalog();
        var (workspace, _) = Create(gated);
        await workspace.RestoreAsync(null, CancellationToken.None);

        var clock = Stopwatch.StartNew();
        workspace.OnWindowShown();
        clock.Stop();

        Assert.NotEmpty(gated.Answers);
        Assert.True(clock.ElapsedMilliseconds < 500, $"창이 뜨는 길에서 {clock.ElapsedMilliseconds}ms 를 기다렸다.");

        // 매달린 조회를 푼다 — 테스트가 끝난 뒤에 답이 도착해도 갈 곳이 있어야 한다.
        foreach (var answer in gated.Answers)
        {
            answer.TrySetResult(null);
        }
    }

    [Fact]
    public async Task OnWindowShown_SurvivesATabThatThrows()
    {
        // 창이 뜨는 길에서 던지면 창이 안 뜬다. 여기서 던지는 것은 앞선 조회를 끊는
        // CancellationTokenSource.Cancel() 이다 — 등록된 콜백이 던지면 그것이 그대로 나온다.
        var (workspace, _) = Create(new ThrowingOnCancelCatalog());
        await workspace.RestoreAsync(null, CancellationToken.None);

        workspace.OnWindowShown();          // 콜백을 등록시킨다
        workspace.OnWindowShown();          // 앞선 조회를 끊는다 → 콜백이 던진다

        Assert.Equal(2, workspace.Panes.Count);
    }

    // ── helper ────────────────────────────────────────────────────

    /// <summary>
    /// 페인 셋 중 둘을 접고, 남은 페인에 배경 탭 하나를 만든다 — <b>보이는 활성 탭만</b>
    /// 미는 구현을 잡는 모양이다.
    /// </summary>
    private static async Task<(PaneViewModel Background, PaneTabsViewModel Folded)> SpreadOutAsync(
        WorkspaceViewModel workspace)
    {
        workspace.SetSplitCommand.Execute(3);
        await workspace.SplitWork;

        var tabs = workspace.LeftTabs();
        var background = tabs.NewTab();

        // 만든 탭이 활성이 된다. 첫 탭으로 돌아가면 방금 만든 것이 배경 탭이 된다.
        tabs.Activate(tabs.Tabs[0]);
        await tabs.SwitchWork;

        var folded = workspace.AllPanes[2];

        workspace.SetSplitCommand.Execute(1);
        await workspace.SplitWork;

        return (background, folded);
    }

    private (WorkspaceViewModel Workspace, SettingsViewModel Settings) Create(IExternalToolCatalog? tools = null)
    {
        var settings = new SettingsViewModel(
            settingsStore, dispatcher, "0.3.1", StateDirectory, new FakeSystemThemeSource());

        return (new WorkspaceViewModel(() => CreatePane(tools), viewStates, null, null, settings), settings);
    }

    private PaneViewModel CreatePane() => CreatePane(null);

    private PaneViewModel CreatePane(IExternalToolCatalog? tools)
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus,
            externalTools: tools ?? catalog, toolLauncher: launcher);

    private static TerminalChoice Choice(TerminalPreset preset) => new(preset);

    /// <summary>답하지 않는 카탈로그. "기다리지 않는가" 를 재는 자리다.</summary>
    private sealed class GatedToolCatalog : IExternalToolCatalog
    {
        private readonly List<TaskCompletionSource<string?>> answers = [];

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

    /// <summary>
    /// 취소되면 던지는 카탈로그. 계약은 던지지 않는 것이지만 (<c>IExternalToolCatalog</c>),
    /// 새는 구현이 창을 못 뜨게 만들면 안 된다.
    /// </summary>
    private sealed class ThrowingOnCancelCatalog : IExternalToolCatalog
    {
        public ValueTask<string?> FindEditorAsync(CancellationToken ct)
        {
            ct.Register(() => throw new InvalidOperationException("탐지 중에 터졌다."));

            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct)
            => ValueTask.FromResult<string?>(null);
    }
}
