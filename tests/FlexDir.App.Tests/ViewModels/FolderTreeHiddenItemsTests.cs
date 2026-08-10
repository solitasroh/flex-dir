using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 트리의 숨김·시스템 필터 (docs/PRD-v2.md §12).
/// <para>
/// <b>목록과 같은 답을 내야 한다.</b> 트리는 원래부터 "숨김·시스템 정책은 열거가 정한 것을
/// 그대로 따른다" 고 적어 놓았는데 <b>열거는 정한 적이 없었다</b> — 그래서 지금까지 트리에
/// <c>$Recycle.Bin</c>·<c>System Volume Information</c> 이 보였다. 판정을
/// <c>ItemVisibility</c> 하나로 모으면 목록과 갈릴 자리가 없다.
/// </para>
/// </summary>
public class FolderTreeHiddenItemsTests
{
    private readonly FakeDriveList drives = new();
    private readonly FakeNetworkPlaceList places = new();
    private readonly FakeFavoriteStore favorites = new();
    private readonly FakeFolderSource folders = new();
    private readonly InlineUiDispatcher dispatcher = new();

    private FolderTreeViewModel CreateTree() => new(drives, places, favorites, folders, dispatcher);

    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    private static FileItem Folder(LocationId parent, string name, FileItemFlags extra = FileItemFlags.None)
        => new(name, parent.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory | extra);

    /// <summary>C:\ 하나를 세우고 그 아래에 보이는 폴더 하나와 숨김 폴더 둘을 둔다.</summary>
    private async Task<(FolderTreeViewModel Tree, TreeNodeViewModel Root)> DriveWithHiddenFoldersAsync(
        bool showHidden = false)
    {
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));

        folders.Folders[root] =
        [
            Folder(root, "Users"),
            Folder(root, "$Recycle.Bin", FileItemFlags.Hidden | FileItemFlags.System),
            Folder(root, "System Volume Information", FileItemFlags.Hidden | FileItemFlags.System),
        ];

        var tree = CreateTree();
        tree.ShowHiddenItems = showHidden;
        await tree.LoadAsync(CancellationToken.None);

        return (tree, tree.Roots[0]);
    }

    [Fact]
    public void New_HidesHiddenItems()
    {
        Assert.False(CreateTree().ShowHiddenItems);
    }

    [Fact]
    public async Task Expand_LeavesOutHiddenAndSystemFolders()
    {
        var (tree, root) = await DriveWithHiddenFoldersAsync();

        await tree.ExpandAsync(root, CancellationToken.None);

        Assert.Equal(["Users"], root.Children.Select(node => node.Label));
    }

    [Fact]
    public async Task Expand_WhenAsked_ShowsThemAll()
    {
        // 사용자가 실물로 확인하는 자리다: 켜면 C:\ 에 $Recycle.Bin 과
        // System Volume Information 이 뜬다.
        var (tree, root) = await DriveWithHiddenFoldersAsync(showHidden: true);

        await tree.ExpandAsync(root, CancellationToken.None);

        Assert.Equal(
            ["$Recycle.Bin", "System Volume Information", "Users"],
            root.Children.Select(node => node.Label));
    }

    [Fact]
    public async Task Expand_WithOnlyHiddenChildren_DropsTheArrow()
    {
        // 열어 봤더니 보여줄 것이 없었다는 뜻이다. 화살표를 남기면 눌러도 아무 일이 없다.
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));
        folders.Folders[root] = [Folder(root, "$Recycle.Bin", FileItemFlags.Hidden | FileItemFlags.System)];

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[0], CancellationToken.None);

        Assert.False(tree.Roots[0].CanExpand);
    }

    // ── 설정을 바꾸면 보고 있던 자리를 지키며 다시 읽는다 ─────────

    [Fact]
    public async Task ReloadFolders_BringsHiddenFoldersInWithoutLosingWhereYouWere()
    {
        // LoadAsync 로 통째로 세우면 펼쳐 둔 것이 전부 접힌다 — 설정 하나 바꿨다고 보고
        // 있던 자리를 잃으면 안 된다 (즐겨찾기가 배운 것과 같은 자리다).
        var (tree, root) = await DriveWithHiddenFoldersAsync();
        await tree.ExpandAsync(root, CancellationToken.None);
        root.IsExpanded = true;

        tree.ShowHiddenItems = true;
        await tree.ReloadFoldersAsync(CancellationToken.None);

        Assert.True(root.IsExpanded);
        Assert.Equal(3, root.Children.Count);
    }

    [Fact]
    public async Task ReloadFolders_KeepsDeeplyExpandedFoldersOpen()
    {
        var root = Path(@"C:\");
        var users = root.Combine("Users");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));
        folders.Folders[root] = [Folder(root, "Users")];
        folders.Folders[users] = [Folder(users, "tester"), Folder(users, "Default", FileItemFlags.Hidden)];

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[0], CancellationToken.None);
        tree.Roots[0].IsExpanded = true;
        await tree.ExpandAsync(tree.Roots[0].Children[0], CancellationToken.None);
        tree.Roots[0].Children[0].IsExpanded = true;

        tree.ShowHiddenItems = true;
        await tree.ReloadFoldersAsync(CancellationToken.None);

        var reloaded = tree.Roots[0].Children[0];

        Assert.True(tree.Roots[0].IsExpanded);
        Assert.True(reloaded.IsExpanded);
        Assert.Equal(["Default", "tester"], reloaded.Children.Select(node => node.Label));
    }

    [Fact]
    public async Task ReloadFolders_DoesNotOpenWhatWasNeverOpened()
    {
        // 펼치지 않은 노드까지 읽으면 트리에 선 모든 폴더에 저장소 호출이 나간다 —
        // 지연 로딩을 고른 이유가 그것이다 (CLAUDE.md §3).
        var (tree, root) = await DriveWithHiddenFoldersAsync();
        folders.EnumerateCalls.Clear();

        tree.ShowHiddenItems = true;
        await tree.ReloadFoldersAsync(CancellationToken.None);

        Assert.Empty(folders.EnumerateCalls);
        Assert.False(root.IsRealized);
    }

    [Fact]
    public async Task ReloadFolders_WhenAFolderCannotBeRead_LeavesTheRestAlone()
    {
        // 권한 없는 폴더·응답 없는 서버가 펼쳐진 채로 있을 수 있다. 트리는 곁다리이고
        // 못 읽는 것이 나머지를 막으면 안 된다.
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));
        drives.Drives.Add(new DriveEntry(Path(@"D:\"), "데이터 (D:)", null));
        folders.Folders[root] = [Folder(root, "Users")];

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[0], CancellationToken.None);
        await tree.ExpandAsync(tree.Roots[1], CancellationToken.None);   // D:\ 는 등록되지 않았다

        tree.ShowHiddenItems = true;
        await tree.ReloadFoldersAsync(CancellationToken.None);

        Assert.Equal(["Users"], tree.Roots[0].Children.Select(node => node.Label));
    }
}
