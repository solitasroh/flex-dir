using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 트리가 활성 페인을 따라간다 (docs/PRD-v2.md §10-3 · 사용자 요청 2026-08-10).
/// <para>
/// <b>잇는 곳은 워크스페이스다.</b> 트리는 페인을 모르고 (§10 이 정한 유일한 출력은
/// <c>NavigationRequested</c> 하나다) 페인은 트리를 모른다 — 즐겨찾기·설정과 같은 구도다.
/// </para>
/// </summary>
public class WorkspaceTreeSyncTests
{
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
    private readonly InlineUiDispatcher dispatcher = new();

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    /// <summary>부모 아래에 폴더를 등록하고 그 자리도 열 수 있게 만든다.</summary>
    private LocationId Sub(LocationId parent, string name)
    {
        var child = parent.Combine(name);

        if (!source.Folders.TryGetValue(parent, out var items))
        {
            items = [];
            source.Folders[parent] = items;
        }

        items.Add(new FileItem(name, child, 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory));
        source.Folders[child] = [];

        return child;
    }

    /// <summary>C:\ 를 루트로 세운 트리와 그 아래 두 폴더.</summary>
    private (WorkspaceViewModel Workspace, FolderTreeViewModel Tree, LocationId Work, LocationId Play) Create()
    {
        var root = Loc(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));

        var work = Sub(root, "work");
        var play = Sub(root, "play");

        var tree = new FolderTreeViewModel(
            drives, new FakeNetworkPlaceList(), new FakeFavoriteStore(), source, dispatcher);

        var workspace = new WorkspaceViewModel(CreatePane(), CreatePane(), viewStates, null, tree);

        return (workspace, tree, work, play);
    }

    private static IEnumerable<TreeNodeViewModel> Walk(TreeNodeViewModel node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var deeper in Walk(child))
            {
                yield return deeper;
            }
        }
    }

    private static LocationId? Selected(FolderTreeViewModel tree)
        => tree.Roots.SelectMany(Walk).FirstOrDefault(node => node.IsSelected)?.Location;

    [Fact]
    public async Task ActivePaneMoves_TreeFollows()
    {
        var (workspace, tree, work, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);

        await workspace.Left.NavigateAsync(work);
        await workspace.TreeRevealWork;

        Assert.Equal(work, Selected(tree));
    }

    [Fact]
    public async Task InactivePaneMoves_TreeStaysPut()
    {
        // 트리는 창에 하나이고 활성 페인을 가리킨다 (§10). 반대편이 움직일 때마다 따라가면
        // 어느 쪽을 보고 있는지 알 수 없고, 그 탐색은 전부 저장소 호출이다.
        var (workspace, tree, work, play) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left.NavigateAsync(work);
        await workspace.TreeRevealWork;

        await workspace.Right.NavigateAsync(play);
        await workspace.TreeRevealWork;

        Assert.Equal(work, Selected(tree));
    }

    [Fact]
    public async Task SwitchingPanes_TreeFollowsTheNewActiveOne()
    {
        // Tab 으로 옮기면 그쪽을 가리켜야 한다 — 안 그러면 트리가 보고 있지 않은 페인의
        // 자리를 계속 보여준다.
        var (workspace, tree, work, play) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left.NavigateAsync(work);
        await workspace.Right.NavigateAsync(play);
        await workspace.TreeRevealWork;

        workspace.SwitchPaneCommand.Execute(null);
        await workspace.TreeRevealWork;

        Assert.Equal(play, Selected(tree));
    }

    [Fact]
    public async Task PickingInTheTree_DoesNotLoop()
    {
        // 여기가 이 기능에서 가장 위험한 자리다. 트리 선택이 페인을 움직이고, 페인이
        // 움직이면 따라가기가 돈다 — 그 따라가기가 다시 선택을 쏘면 끝나지 않는다.
        var (workspace, tree, work, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[0], CancellationToken.None);

        var node = tree.Roots[0].Children.First(child => child.Location.Equals(work));
        var before = source.EnumerateCalls.Count;

        node.IsSelected = true;
        await workspace.TreeRevealWork;

        Assert.Equal(work, workspace.ActivePane.CurrentLocation);

        // 유한하면 된다. 정확한 수를 못박으면 무관한 최적화가 이 테스트를 깨뜨린다.
        Assert.True(
            source.EnumerateCalls.Count - before < 10,
            $"따라가기가 되먹임을 만들었다 — 열거 {source.EnumerateCalls.Count - before}회.");
    }

    [Fact]
    public async Task PaneOutsideTheTree_LeavesTheSelectionAlone()
    {
        // 주소줄로 트리에 없는 드라이브를 열 수 있다. 그때 선택을 지우면 방금까지 보던
        // 자리를 잃는다 — 아무것도 하지 않는 편이 낫다.
        var (workspace, tree, work, _) = Create();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left.NavigateAsync(work);
        await workspace.TreeRevealWork;

        var elsewhere = Loc(@"D:\elsewhere");
        source.Folders[elsewhere] = [];

        await workspace.Left.NavigateAsync(elsewhere);
        await workspace.TreeRevealWork;

        Assert.Equal(work, Selected(tree));
    }

    [Fact]
    public async Task WithNoTreeWired_NothingHappens()
    {
        // 트리 없이 조립되는 경우가 있다 (선택 인자).
        var folder = Loc(@"C:\work");
        source.Folders[folder] = [];

        var workspace = new WorkspaceViewModel(CreatePane(), CreatePane(), viewStates);

        await workspace.Left.NavigateAsync(folder);
        await workspace.TreeRevealWork;

        Assert.Equal(folder, workspace.Left.CurrentLocation);
    }
}
