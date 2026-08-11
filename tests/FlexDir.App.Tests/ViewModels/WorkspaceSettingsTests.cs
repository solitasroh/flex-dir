using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Settings;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 설정이 창 전체에 닿는 자리 (docs/PRD-v2.md §12).
/// <para>
/// <b>워크스페이스가 잇는다.</b> 설정은 페인도 트리도 모르고 (<c>SettingsViewModel</c>),
/// 페인과 트리는 값을 받아 거르기만 한다 — 언제 다시 읽을지를 정하는 곳이 하나여야
/// 클릭 한 번에 저장소 호출이 쏟아지지 않는다. 트리의 <c>NavigationRequested</c> 를 잇는
/// 것과 같은 구도다.
/// </para>
/// </summary>
public class WorkspaceSettingsTests
{
    private const string StateDirectory = @"C:\Users\tester\AppData\Roaming\flex-dir";

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
    private readonly FakeDriveList drives = new();
    private readonly FakeSettingsStore settingsStore = new();
    private readonly InlineUiDispatcher dispatcher = new();

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    private (WorkspaceViewModel Workspace, SettingsViewModel Settings, FolderTreeViewModel Tree) Create()
    {
        var tree = new FolderTreeViewModel(
            drives, new FakeNetworkPlaceList(), new FakeFavoriteStore(), source, dispatcher);

        var settings = new SettingsViewModel(settingsStore, dispatcher, "0.3.1", StateDirectory);

        return (new WorkspaceViewModel(CreatePane, viewStates, null, tree, settings), settings, tree);
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    private LocationId Folder(string path, params (string Name, FileItemFlags Flags)[] entries)
    {
        var folder = Loc(path);

        source.Folders[folder] =
        [
            .. entries.Select(entry => new FileItem(
                entry.Name,
                folder.Combine(entry.Name),
                0,
                DateTimeOffset.UnixEpoch,
                entry.Flags)),
        ];

        return folder;
    }

    // ── 시작 폴더 ─────────────────────────────────────────────────

    [Fact]
    public async Task Restore_InFixedMode_OpensThatFolderInsteadOfTheLastOne()
    {
        var last = Folder(@"C:\last", ("a.txt", FileItemFlags.None));
        var chosen = Folder(@"C:\work", ("b.txt", FileItemFlags.None));
        await viewStates.SaveGlobalAsync(
            new GlobalViewState(0.5, null, PaneTabsState.Single(last), PaneTabsState.Single(last)),
            CancellationToken.None);
        settingsStore.Seed(new AppSettings { StartMode = StartFolderMode.Fixed, StartFolder = chosen });

        var (workspace, _, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(chosen, workspace.Left.CurrentLocation);
        Assert.Equal(chosen, workspace.Right.CurrentLocation);
    }

    [Fact]
    public async Task Restore_InLastFolderMode_KeepsWhatEachPaneWasLookingAt()
    {
        // 두 페인이 서로 다른 폴더를 기억한다. 설정이 그것을 뭉개면 안 된다.
        var left = Folder(@"C:\left", ("a.txt", FileItemFlags.None));
        var right = Folder(@"C:\right", ("b.txt", FileItemFlags.None));
        await viewStates.SaveGlobalAsync(
            new GlobalViewState(0.5, null, PaneTabsState.Single(left), PaneTabsState.Single(right)),
            CancellationToken.None);

        var (workspace, _, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(left, workspace.Left.CurrentLocation);
        Assert.Equal(right, workspace.Right.CurrentLocation);
    }

    [Fact]
    public async Task Restore_WithNoSettingsWired_StillOpensTheLastFolder()
    {
        // 조립이 설정 없이 서는 경우가 있다 (트리·업데이트와 같은 이유로 선택 인자다).
        var last = Folder(@"C:\last", ("a.txt", FileItemFlags.None));
        await viewStates.SaveGlobalAsync(
            new GlobalViewState(0.5, null, PaneTabsState.Single(last), PaneTabsState.Single(last)),
            CancellationToken.None);

        var workspace = new WorkspaceViewModel(CreatePane, viewStates);
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(last, workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task Restore_LoadsTheSettingsSoThePanelShowsThemToo()
    {
        // 창을 열고 설정을 처음 펼쳤을 때 저장된 값이 이미 들어 있어야 한다.
        settingsStore.Seed(new AppSettings { ShowHiddenItems = true });

        var (workspace, settings, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.True(settings.ShowHiddenItems);
    }

    // ── 숨김 파일 보기 ────────────────────────────────────────────

    [Fact]
    public async Task Restore_HandsTheHiddenPolicyToBothPanesAndTheTree()
    {
        settingsStore.Seed(new AppSettings { ShowHiddenItems = true });

        var (workspace, _, tree) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.True(workspace.Left.ShowHiddenItems);
        Assert.True(workspace.Right.ShowHiddenItems);
        Assert.True(tree.ShowHiddenItems);
    }

    [Fact]
    public async Task Restore_AppliesThePolicyBeforeOpeningTheFolders()
    {
        // 순서가 뒤집히면 시작할 때 한 번은 옛 정책으로 그려지고, 그 뒤 아무도 다시 읽지
        // 않으므로 숨김 항목이 그대로 남는다.
        var folder = Folder(
            @"C:\work",
            ("보이는.txt", FileItemFlags.None),
            ("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System));
        settingsStore.Seed(new AppSettings { ShowHiddenItems = true });

        var (workspace, _, _) = Create();
        await workspace.RestoreAsync(folder, CancellationToken.None);

        Assert.Equal(2, workspace.Left.Items.Count);

        // 페인마다 한 번씩이다. 정책을 나중에 밀고 다시 읽으면 여기가 넷이 된다 — 시작
        // 경로에서 폴더를 두 번 읽는 것은 cold start 예산에 그대로 얹힌다 (ADR-003).
        Assert.Equal(2, source.EnumerateCalls.Count(call => call.Equals(folder)));
    }

    [Fact]
    public async Task TogglingHiddenItems_RereadsBothPanes()
    {
        var folder = Folder(
            @"C:\work",
            ("보이는.txt", FileItemFlags.None),
            ("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System));
        var (workspace, settings, _) = Create();
        await workspace.RestoreAsync(folder, CancellationToken.None);

        settings.ShowHiddenItems = true;
        await workspace.HiddenItemsWork;

        Assert.Equal(2, workspace.Left.Items.Count);
        Assert.Equal(2, workspace.Right.Items.Count);
    }

    [Fact]
    public async Task TogglingHiddenItems_RereadsTheOpenedPartsOfTheTree()
    {
        var root = Loc(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));
        source.Folders[root] =
        [
            new FileItem("Users", root.Combine("Users"), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory),
            new FileItem(
                "$Recycle.Bin",
                root.Combine("$Recycle.Bin"),
                0,
                DateTimeOffset.UnixEpoch,
                FileItemFlags.Directory | FileItemFlags.Hidden | FileItemFlags.System),
        ];

        var (workspace, settings, tree) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[0], CancellationToken.None);
        tree.Roots[0].IsExpanded = true;

        settings.ShowHiddenItems = true;
        await workspace.HiddenItemsWork;

        Assert.Equal(["$Recycle.Bin", "Users"], tree.Roots[0].Children.Select(node => node.Label));
        Assert.True(tree.Roots[0].IsExpanded);
    }

    // ── 상태 폴더 열기 ────────────────────────────────────────────

    [Fact]
    public async Task OpenStateFolder_TakesTheActivePaneThere()
    {
        var stateFolder = Folder(StateDirectory, ("settings.json", FileItemFlags.None));
        var (workspace, settings, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        workspace.ActivateCommand.Execute(PaneSide.Right);

        settings.OpenStateFolderCommand.Execute(null);

        Assert.Equal(stateFolder, workspace.Right.CurrentLocation);
        Assert.Null(workspace.Left.CurrentLocation);
    }

    // ── 현재 폴더로 ───────────────────────────────────────────────

    [Fact]
    public async Task UseCurrentFolderAsStart_TakesTheActivePanesFolder()
    {
        var folder = Folder(@"C:\work", ("a.txt", FileItemFlags.None));
        var (workspace, settings, _) = Create();
        await workspace.RestoreAsync(folder, CancellationToken.None);

        workspace.UseCurrentFolderAsStartCommand.Execute(null);
        await settings.SaveWork;

        Assert.True(settings.StartsAtFixedFolder);
        Assert.Equal(folder, settingsStore.Current.StartFolder);
    }

    [Fact]
    public void UseCurrentFolderAsStart_WithNoSettingsWired_DoesNothing()
    {
        var workspace = new WorkspaceViewModel(CreatePane, viewStates);

        workspace.UseCurrentFolderAsStartCommand.Execute(null);

        Assert.Equal(0, settingsStore.Saves);
    }
}
