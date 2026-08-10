using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Favorites;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 창 왼쪽의 폴더 트리 (docs/PRD-v2.md §10 · 사용자 결정 2026-08-10).
/// <para>
/// 루트는 드라이브와 <b>매핑 드라이브에서 뽑은 서버</b>다 — 네트워크 이웃을 훑지 않는다.
/// 자식은 펼칠 때 읽고, 폴더만 선다.
/// </para>
/// </summary>
public class FolderTreeViewModelTests
{
    private readonly FakeDriveList drives = new();
    private readonly FakeNetworkPlaceList places = new();
    private readonly FakeFavoriteStore favorites = new();
    private readonly FakeFolderSource folders = new();
    private readonly InlineUiDispatcher dispatcher = new();

    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task LoadAsync_PutsEveryDriveAtTheRoot()
    {
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));
        drives.Drives.Add(new DriveEntry(Path(@"D:\"), "데이터 (D:)", null));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        Assert.Equal(["로컬 디스크 (C:)", "데이터 (D:)"], tree.Roots.Select(node => node.Label));
    }

    [Fact]
    public async Task LoadAsync_ForTwoDrivesOnOneServer_AddsThatServerOnce()
    {
        // 같은 NAS 를 두 글자에 매핑하는 것은 흔하다. 서버가 두 번 서면 같은 것을 두 번
        // 펼치게 된다.
        var server = Path(@"\\10.10.10.23");
        drives.Drives.Add(new DriveEntry(Path(@"Y:\"), "공유 (Y:)", server));
        drives.Drives.Add(new DriveEntry(Path(@"Z:\"), "nas (Z:)", server));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        Assert.Single(tree.Roots, node => node.Location.IsNetworkServer);
    }

    [Fact]
    public async Task LoadAsync_PutsServersAfterTheDrives()
    {
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));
        drives.Drives.Add(new DriveEntry(Path(@"Z:\"), "nas (Z:)", Path(@"\\10.10.10.23")));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        Assert.Equal([@"C:\", @"Z:\", @"\\10.10.10.23"], tree.Roots.Select(node => node.Location.DisplayPath));
    }

    [Fact]
    public async Task LoadAsync_PutsNetworkPlacesLast()
    {
        // '네트워크 위치 추가' 로 등록한 곳은 드라이브 문자가 없어 매핑에서 뽑히지 않는다
        // (2026-08-10 사용자 지적). 등록 이름 그대로 맨 아래 선다.
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", Path(@"\\10.10.10.23")));
        places.Places.Add(new NetworkPlace(Path(@"\\10.10.20.30\rsj0811"), "DEV"));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        Assert.Equal(["로컬 디스크 (C:)", @"\\10.10.10.23", "DEV"], tree.Roots.Select(node => node.Label));
    }

    [Fact]
    public async Task LoadAsync_ForADeepNetworkPlace_KeepsTheWholePath()
    {
        // 깊은 경로를 이름 하나로 부르려고 등록한 것이다. 서버로 접으면 그 이유가 사라진다.
        places.Places.Add(new NetworkPlace(Path(@"\\10.10.20.30\rsj0811\workspace\work"), "dev-server"));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        var node = Assert.Single(tree.Roots);

        Assert.Equal("dev-server", node.Label);
        Assert.Equal(@"\\10.10.20.30\rsj0811\workspace\work", node.Location.DisplayPath);
    }

    [Fact]
    public async Task LoadAsync_WithNoDrivesAtAll_IsEmptyNotAThrow()
    {
        var tree = CreateTree();

        await tree.LoadAsync(CancellationToken.None);

        Assert.Empty(tree.Roots);
    }

    [Fact]
    public async Task LoadAsync_Twice_DoesNotDuplicate()
    {
        // 드라이브가 붙거나 빠지면 다시 부른다. 더하면 같은 드라이브가 두 번 선다.
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);
        await tree.LoadAsync(CancellationToken.None);

        Assert.Single(tree.Roots);
    }

    [Fact]
    public async Task ExpandAsync_ShowsFoldersAndLeavesFilesOut()
    {
        var root = Path(@"C:\");
        folders.Folders[root] =
        [
            Folder(root, "Users"),
            File(root, "pagefile.sys"),
            Folder(root, "Windows"),
        ];

        var node = await FirstRootAsync(root);
        await CreateTree().ExpandAsync(node, CancellationToken.None);

        Assert.Equal(["Users", "Windows"], node.Children.Select(child => child.Label));
    }

    [Fact]
    public async Task ExpandAsync_SortsTheFoldersNaturally()
    {
        // 목록과 같은 규칙이어야 한다 — 트리에서 10 이 2 앞에 서면 같은 폴더가 두 곳에서
        // 다르게 보인다.
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "step10"), Folder(root, "step2"), Folder(root, "step1")];

        var node = await FirstRootAsync(root);
        await CreateTree().ExpandAsync(node, CancellationToken.None);

        Assert.Equal(["step1", "step2", "step10"], node.Children.Select(child => child.Label));
    }

    [Fact]
    public async Task ExpandAsync_Twice_EnumeratesOnce()
    {
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "Users")];

        var tree = CreateTree();
        var node = await FirstRootAsync(root, tree);

        await tree.ExpandAsync(node, CancellationToken.None);
        await tree.ExpandAsync(node, CancellationToken.None);

        Assert.Single(folders.EnumerateCalls);
    }

    [Fact]
    public async Task ExpandAsync_WhenTheFolderCannotBeRead_StaysEmptyWithoutThrowing()
    {
        // 권한 없는 폴더·사라진 드라이브. 트리는 곁다리이고, 못 읽는 것이 앱을 멈추는
        // 사건이 되면 안 된다 (FakeFolderSource 는 등록되지 않은 폴더를 NotFound 로 낸다).
        var node = await FirstRootAsync(Path(@"C:\"));

        await CreateTree().ExpandAsync(node, CancellationToken.None);

        Assert.Empty(node.Children);
        Assert.False(node.CanExpand);
    }

    [Fact]
    public async Task SelectingANode_AsksToNavigateThere()
    {
        var root = Path(@"C:\");
        LocationId? asked = null;

        var tree = CreateTree();
        tree.NavigationRequested += (_, location) => asked = location;

        var node = await FirstRootAsync(root, tree);
        node.IsSelected = true;

        Assert.Equal(root, asked);
    }

    [Fact]
    public async Task ExpandingThroughTheProperty_LoadsTheChildren()
    {
        // 배선 확인이다 — View 는 IsExpanded 만 묶는다. 노드가 트리를 부르지 않으면
        // 화면에서만 아무 일도 일어나지 않는다.
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "Users")];

        var node = await FirstRootAsync(root);
        node.IsExpanded = true;

        Assert.Equal(["Users"], node.Children.Select(child => child.Label));
    }

    // ── 즐겨찾기 (docs/PRD-v2.md §10-2 · 사용자 결정 2026-08-10) ────

    [Fact]
    public async Task LoadAsync_PutsFavoritesAtTheVeryTop()
    {
        // 가장 자주 가는 곳이 눈과 마우스에 제일 가깝다 (사용자 결정) — 드라이브가 많은
        // 기계에서 아래에 두면 스크롤해야 보인다.
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));
        await favorites.SaveAsync([new Favorite(Path(@"C:\work"), "작업")], CancellationToken.None);

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        Assert.Equal(["작업", "로컬 디스크 (C:)"], tree.Roots.Select(node => node.Label));
        Assert.True(tree.Roots[0].IsFavorite);
        Assert.False(tree.Roots[1].IsFavorite);
    }

    [Fact]
    public async Task AddFavoriteAsync_PutsItAtTheTopAndSaves()
    {
        var tree = await LoadedTreeAsync();

        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        Assert.Equal("work", tree.Roots[0].Label);
        Assert.True(tree.Roots[0].IsFavorite);

        // 즉시 남긴다 — 창을 강제로 끄더라도 방금 넣은 것이 사라지면 안 된다.
        Assert.Equal(1, favorites.Saves);
        Assert.Equal([@"C:\work"], favorites.Current.Select(f => f.Path.DisplayPath));
    }

    [Fact]
    public async Task AddFavoriteAsync_TheSamePathTwice_AddsItOnce()
    {
        var tree = await LoadedTreeAsync();

        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);
        await tree.AddFavoriteAsync(Path(@"C:\WORK"), CancellationToken.None);

        // 파일시스템이 대소문자를 구분하지 않으므로 같은 폴더다.
        Assert.Single(tree.Roots, node => node.IsFavorite);
        Assert.Equal(1, favorites.Saves);
    }

    [Fact]
    public async Task AddFavoriteAsync_DoesNotCollapseWhatIsAlreadyOpen()
    {
        // 루트를 통째로 다시 세우면 펼쳐 둔 폴더가 전부 접힌다 — 즐겨찾기 하나 넣었다고
        // 보고 있던 자리를 잃으면 안 된다.
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "Users")];

        var tree = await LoadedTreeAsync(root);
        var drive = tree.Roots[0];
        await tree.ExpandAsync(drive, CancellationToken.None);

        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        Assert.Same(drive, tree.Roots[1]);
        Assert.True(drive.IsRealized);
        Assert.Single(drive.Children);
    }

    [Fact]
    public async Task RemoveFavoriteAsync_TakesItOutAndSaves()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        await tree.RemoveFavoriteAsync(tree.Roots[0], CancellationToken.None);

        Assert.DoesNotContain(tree.Roots, node => node.IsFavorite);
        Assert.Empty(favorites.Current);
    }

    [Fact]
    public async Task RemoveFavoriteAsync_OnSomethingThatIsNotAFavorite_DoesNothing()
    {
        // 드라이브는 뺄 수 없다. 메뉴가 막더라도 여기서 한 번 더 막는다.
        var tree = await LoadedTreeAsync();

        await tree.RemoveFavoriteAsync(tree.Roots[0], CancellationToken.None);

        Assert.Single(tree.Roots);
        Assert.Equal(0, favorites.Saves);
    }

    [Fact]
    public async Task RenameFavoriteAsync_ChangesTheLabelOnly()
    {
        // 같은 이름의 폴더가 여럿일 때 가르는 유일한 수단이다. 경로는 그대로다.
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        await tree.RenameFavoriteAsync(tree.Roots[0], "펌웨어", CancellationToken.None);

        Assert.Equal("펌웨어", tree.Roots[0].Label);
        Assert.Equal(@"C:\work", tree.Roots[0].Location.DisplayPath);
        Assert.Equal("펌웨어", favorites.Current[0].Label);
    }

    [Fact]
    public async Task RenameFavoriteAsync_WithBlank_KeepsTheOldLabel()
    {
        // 빈 라벨은 트리에 빈 줄로 선다. 편집을 지우고 확정한 것은 취소로 본다.
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        await tree.RenameFavoriteAsync(tree.Roots[0], "   ", CancellationToken.None);

        Assert.Equal("work", tree.Roots[0].Label);
    }

    [Fact]
    public async Task MoveFavoriteAsync_ReordersAndSaves()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\a"), CancellationToken.None);
        await tree.AddFavoriteAsync(Path(@"C:\b"), CancellationToken.None);

        // 추가 순서대로 선다: a · b
        await tree.MoveFavoriteAsync(tree.Roots[1], -1, CancellationToken.None);

        Assert.Equal(["b", "a"], tree.Roots.Where(n => n.IsFavorite).Select(n => n.Label));
        Assert.Equal(["b", "a"], favorites.Current.Select(f => f.Label));
    }

    [Fact]
    public async Task MoveFavoriteAsync_PastTheEdge_DoesNothing()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\a"), CancellationToken.None);
        var saves = favorites.Saves;

        await tree.MoveFavoriteAsync(tree.Roots[0], -1, CancellationToken.None);

        Assert.Equal("a", tree.Roots[0].Label);
        Assert.Equal(saves, favorites.Saves);
    }

    [Fact]
    public async Task AddFavoriteAsync_WhenSavingFails_KeepsItOnScreen()
    {
        // 저장이 실패해도 앱이 죽거나 방금 한 조작이 화면에서 사라지면 안 된다.
        // 다음 조작이 다시 저장을 시도한다.
        var tree = await LoadedTreeAsync();
        favorites.SaveFailure = new IOException("디스크가 꽉 찼다");

        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        Assert.True(tree.Roots[0].IsFavorite);
    }

    [Fact]
    public async Task SelectingAFavorite_AsksToNavigateThere()
    {
        var tree = await LoadedTreeAsync();
        LocationId? asked = null;
        tree.NavigationRequested += (_, location) => asked = location;

        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);
        tree.Roots[0].IsSelected = true;

        Assert.Equal(Path(@"C:\work"), asked);
    }

    // ── 목록에서 트리로 드래그 ────────────────────────────────────

    [Fact]
    public async Task AddFavoritesAsync_TakesFoldersAndLeavesFilesOut()
    {
        // 파일을 고정하면 트리에서 펼칠 수도 없고 골라도 페인이 열지 못한다.
        // 무엇이 폴더인지는 파일시스템에 물어야 알고, 그 물음은 UI 스레드 밖이다.
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "work"), File(root, "note.txt")];

        var tree = await LoadedTreeAsync(root);

        await tree.AddFavoritesAsync([@"C:\work", @"C:\note.txt"], CancellationToken.None);

        Assert.Equal(["work"], tree.Roots.Where(n => n.IsFavorite).Select(n => n.Label));
    }

    [Fact]
    public async Task AddFavoritesAsync_SkipsWhatIsNotThere()
    {
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "work")];

        var tree = await LoadedTreeAsync(root);

        await tree.AddFavoritesAsync([@"C:\없는폴더"], CancellationToken.None);

        Assert.DoesNotContain(tree.Roots, node => node.IsFavorite);
    }

    [Fact]
    public async Task AddFavoritesAsync_SkipsPathsThatDoNotParse()
    {
        var tree = await LoadedTreeAsync();

        await tree.AddFavoritesAsync(["", "??"], CancellationToken.None);

        Assert.DoesNotContain(tree.Roots, node => node.IsFavorite);
    }

    [Fact]
    public async Task AddFavoritesAsync_ManyAtOnce_SavesOnce()
    {
        // 열 개를 끌어다 놓으면 저장도 열 번이면 안 된다.
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "a"), Folder(root, "b")];

        var tree = await LoadedTreeAsync(root);

        await tree.AddFavoritesAsync([@"C:\a", @"C:\b"], CancellationToken.None);

        Assert.Equal(["a", "b"], tree.Roots.Where(n => n.IsFavorite).Select(n => n.Label));
        Assert.Equal(1, favorites.Saves);
    }

    [Fact]
    public async Task AddFavoritesAsync_WithNothingNew_DoesNotSave()
    {
        var root = Path(@"C:\");
        folders.Folders[root] = [Folder(root, "work")];

        var tree = await LoadedTreeAsync(root);
        await tree.AddFavoritesAsync([@"C:\work"], CancellationToken.None);
        var saves = favorites.Saves;

        await tree.AddFavoritesAsync([@"C:\work"], CancellationToken.None);

        Assert.Equal(saves, favorites.Saves);
    }

    // ── 즐겨찾기 이름 편집 (트리 우클릭 → 이름 바꾸기) ──────────────

    [Fact]
    public async Task BeginRename_LoadsTheCurrentLabelIntoTheEditor()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);
        var node = tree.Roots[0];

        tree.BeginRenameCommand.Execute(node);

        Assert.True(node.IsEditing);
        Assert.Equal("work", node.EditingLabel);
    }

    [Fact]
    public async Task CommitRename_AppliesAndClosesTheEditor()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);
        var node = tree.Roots[0];

        tree.BeginRenameCommand.Execute(node);
        await tree.CommitRenameCommand.ExecuteAsync("펌웨어");

        Assert.False(node.IsEditing);
        Assert.Equal("펌웨어", tree.Roots[0].Label);
        Assert.Equal("펌웨어", favorites.Current[0].Label);
    }

    [Fact]
    public async Task CancelRename_LeavesTheLabelAlone()
    {
        // 포커스를 잃는 것도 취소다 (목록 이름변경과 같은 규칙 — docs/DESIGN.md §9-1).
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);
        var node = tree.Roots[0];

        tree.BeginRenameCommand.Execute(node);
        node.EditingLabel = "버릴 것";
        tree.CancelRenameCommand.Execute(null);

        Assert.False(node.IsEditing);
        Assert.Equal("work", tree.Roots[0].Label);
    }

    [Fact]
    public async Task CancelRename_Twice_IsFine()
    {
        // 확정으로 닫힌 뒤에도 포커스 상실이 한 번 더 온다. 멱등이어야 한다.
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        tree.CancelRenameCommand.Execute(null);
        tree.CancelRenameCommand.Execute(null);
    }

    [Fact]
    public async Task CommitRename_WithNothingBeingEdited_DoesNothing()
    {
        // 포커스 상실이 확정 뒤에 한 번 더 오는 경로다.
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        await tree.CommitRenameCommand.ExecuteAsync("아무거나");

        Assert.Equal("work", tree.Roots[0].Label);
    }

    [Fact]
    public async Task BeginRename_OnSomethingThatIsNotAFavorite_DoesNothing()
    {
        // 드라이브 이름은 시스템이 주는 것이라 우리가 정할 것이 없다.
        var tree = await LoadedTreeAsync();

        tree.BeginRenameCommand.Execute(tree.Roots[0]);

        Assert.False(tree.Roots[0].IsEditing);
    }

    [Fact]
    public async Task PinCommand_OnAnOrdinaryNode_PinsIt()
    {
        // 트리 우클릭의 진입점이다.
        var tree = await LoadedTreeAsync();

        await tree.PinCommand.ExecuteAsync(tree.Roots.Last());

        Assert.True(tree.Roots[0].IsFavorite);
        Assert.Equal(@"C:\", tree.Roots[0].Location.DisplayPath);
    }

    [Fact]
    public async Task UnpinCommand_TakesItOut()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\work"), CancellationToken.None);

        await tree.UnpinCommand.ExecuteAsync(tree.Roots[0]);

        Assert.DoesNotContain(tree.Roots, node => node.IsFavorite);
    }

    [Fact]
    public async Task MoveUpAndDownCommands_Reorder()
    {
        var tree = await LoadedTreeAsync();
        await tree.AddFavoriteAsync(Path(@"C:\a"), CancellationToken.None);
        await tree.AddFavoriteAsync(Path(@"C:\b"), CancellationToken.None);

        await tree.MoveUpCommand.ExecuteAsync(tree.Roots[1]);

        Assert.Equal(["b", "a"], tree.Roots.Where(n => n.IsFavorite).Select(n => n.Label));

        await tree.MoveDownCommand.ExecuteAsync(tree.Roots[0]);

        Assert.Equal(["a", "b"], tree.Roots.Where(n => n.IsFavorite).Select(n => n.Label));
    }

    [Fact]
    public void Width_BelowTheMinimum_IsClamped()
    {
        // 스플리터를 끝까지 끌거나 저장 파일이 손상돼도 트리가 사라지면 안 된다 —
        // SplitterRatio 와 같은 판단이다.
        var tree = CreateTree();

        tree.Width = 10;

        Assert.True(tree.Width >= 120, $"트리 폭이 {tree.Width} 로 접혔다.");
    }

    [Fact]
    public void IsVisible_StartsShown()
    {
        Assert.True(CreateTree().IsVisible);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private FolderTreeViewModel CreateTree() => new(drives, places, favorites, folders, dispatcher);

    /// <summary>드라이브 하나를 등록하고 그 루트 노드를 낸다.</summary>
    private async Task<TreeNodeViewModel> FirstRootAsync(LocationId root, FolderTreeViewModel? tree = null)
    {
        if (drives.Drives.Count == 0)
        {
            drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));
        }

        tree ??= CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        return tree.Roots[0];
    }

    /// <summary>드라이브 하나를 세워 둔 트리. 즐겨찾기 테스트가 그 위에 얹는다.</summary>
    private async Task<FolderTreeViewModel> LoadedTreeAsync(LocationId? root = null)
    {
        drives.Drives.Add(new DriveEntry(root ?? Path(@"C:\"), "로컬 디스크 (C:)", null));

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        return tree;
    }

    private static FileItem Folder(LocationId parent, string name)
        => new(name, parent.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory);

    private static FileItem File(LocationId parent, string name)
        => new(name, parent.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None);
}
