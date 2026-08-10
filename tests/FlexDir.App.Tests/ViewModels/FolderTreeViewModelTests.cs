using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

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

    private FolderTreeViewModel CreateTree() => new(drives, places, folders, dispatcher);

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

    private static FileItem Folder(LocationId parent, string name)
        => new(name, parent.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory);

    private static FileItem File(LocationId parent, string name)
        => new(name, parent.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None);
}
