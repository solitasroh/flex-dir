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
/// 트리가 페인을 따라간다 (docs/PRD-v2.md §10-3 · 사용자 요청 2026-08-10).
/// <para>
/// <b>§10 이 반대로 정해 둔 것을 뒤집었다.</b> 그때의 근거는 "경로를 따라 노드를 여는 것이
/// 전부 저장소 호출이다" 였고 그 비용은 그대로다 — 그래서 <b>이미 연 단계는 다시 읽지
/// 않고</b>, 새 탐색이 오면 진행 중인 따라가기를 취소한다.
/// </para>
/// </summary>
public class FolderTreeRevealTests
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

    /// <summary>부모 아래에 폴더를 등록하고 그 자리도 열 수 있게 만든다.</summary>
    private LocationId Sub(LocationId parent, string name)
    {
        var child = parent.Combine(name);

        if (!folders.Folders.TryGetValue(parent, out var items))
        {
            items = [];
            folders.Folders[parent] = items;
        }

        items.Add(new FileItem(name, child, 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory));
        folders.Folders[child] = [];

        return child;
    }

    /// <summary>C:\ 하나를 세운 트리와 <c>C:\Users\SOOJANG\work</c> 를 만든다.</summary>
    private async Task<(FolderTreeViewModel Tree, LocationId Deep)> DeepTreeAsync()
    {
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));

        var users = Sub(root, "Users");
        var user = Sub(users, "SOOJANG");
        var work = Sub(user, "work");

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        return (tree, work);
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

    private static TreeNodeViewModel? Selected(FolderTreeViewModel tree)
        => tree.Roots.SelectMany(Walk).FirstOrDefault(node => node.IsSelected);

    // ── 경로를 따라 연다 ──────────────────────────────────────────

    [Fact]
    public async Task Reveal_OpensEveryStepAndSelectsTheFolder()
    {
        var (tree, work) = await DeepTreeAsync();

        await tree.RevealAsync(work, CancellationToken.None);

        Assert.Equal(work, Selected(tree)?.Location);

        // 조상이 전부 펼쳐져 있어야 고른 것이 화면에 보인다.
        var users = tree.Roots[0].Children[0];

        Assert.True(tree.Roots[0].IsExpanded);
        Assert.True(users.IsExpanded);
        Assert.True(users.Children[0].IsExpanded);
    }

    [Fact]
    public async Task Reveal_PublishesThePathItWalked()
    {
        // View 가 이것을 따라 내려간다 (Views/TreeScroll.cs). 가상화된 TreeView 에서는
        // 부모 컨테이너가 실현돼 있어야 자식 컨테이너를 얻을 수 있고, 그래서 "어느 노드를
        // 골랐나" 만으로는 부족하다 — 거기까지 가는 길이 필요하다.
        var (tree, work) = await DeepTreeAsync();

        await tree.RevealAsync(work, CancellationToken.None);

        Assert.Equal(
            [@"C:\", @"C:\Users", @"C:\Users\SOOJANG", @"C:\Users\SOOJANG\work"],
            tree.RevealedPath.Select(node => node.Location.DisplayPath));
    }

    [Fact]
    public async Task Reveal_ToAFolderOutsideEveryRoot_LeavesThePathAlone()
    {
        // 아무것도 하지 않는 경로다. 길을 비우면 View 가 이전 선택을 화면에서 놓친다.
        var (tree, work) = await DeepTreeAsync();
        await tree.RevealAsync(work, CancellationToken.None);

        await tree.RevealAsync(Path(@"D:\somewhere"), CancellationToken.None);

        Assert.Equal(work, tree.RevealedPath[^1].Location);
    }

    [Fact]
    public async Task Reveal_ToARootItself_SelectsThatRoot()
    {
        var (tree, _) = await DeepTreeAsync();

        await tree.RevealAsync(Path(@"C:\"), CancellationToken.None);

        Assert.Equal(Path(@"C:\"), Selected(tree)?.Location);
    }

    [Fact]
    public async Task Reveal_DoesNotRereadStepsThatAreAlreadyOpen()
    {
        // §10 이 반대한 이유가 저장소 호출이다. 두 번째 따라가기가 같은 값을 다시 내면
        // 폴더를 옮길 때마다 경로 깊이만큼 열거가 반복된다.
        var (tree, work) = await DeepTreeAsync();
        await tree.RevealAsync(work, CancellationToken.None);
        folders.EnumerateCalls.Clear();

        await tree.RevealAsync(work, CancellationToken.None);

        Assert.Empty(folders.EnumerateCalls);
    }

    [Fact]
    public async Task Reveal_ToAFolderOutsideEveryRoot_DoesNothing()
    {
        // 트리에 없는 드라이브를 페인이 열 수 있다 (주소줄로 친 경로).
        var (tree, _) = await DeepTreeAsync();
        folders.EnumerateCalls.Clear();

        await tree.RevealAsync(Path(@"D:\somewhere"), CancellationToken.None);

        Assert.Null(Selected(tree));
        Assert.Empty(folders.EnumerateCalls);
    }

    [Fact]
    public async Task Reveal_WhenAStepCannotBeRead_StopsThereWithoutThrowing()
    {
        // 권한 없는 폴더가 중간에 있을 수 있다. 트리는 곁다리이고 (ExpandAsync 와 같은
        // 판단) 못 읽는 것이 페인의 탐색을 망치면 안 된다.
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));

        var users = Sub(root, "Users");
        folders.Folders.Remove(users);   // 등록되지 않은 폴더는 NotFound 다

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);

        await tree.RevealAsync(users.Combine("SOOJANG"), CancellationToken.None);

        // 갈 수 있는 데까지는 갔다 — 사용자가 어디쯤인지는 볼 수 있다.
        Assert.Equal(users, Selected(tree)?.Location);
    }

    [Fact]
    public async Task Reveal_PrefersTheRootThatIsClosest()
    {
        // 즐겨찾기 C:\Users 와 드라이브 C:\ 둘 다 C:\Users\SOOJANG 을 담는다. 가까운
        // 쪽에서 출발하면 여는 단계가 줄고, 그것이 그대로 저장소 호출 수다.
        var root = Path(@"C:\");
        drives.Drives.Add(new DriveEntry(root, "로컬 디스크 (C:)", null));

        var users = Sub(root, "Users");
        var user = Sub(users, "SOOJANG");

        await favorites.SaveAsync([new Favorite(users, "사용자")], CancellationToken.None);

        var tree = CreateTree();
        await tree.LoadAsync(CancellationToken.None);
        folders.EnumerateCalls.Clear();

        await tree.RevealAsync(user, CancellationToken.None);

        // 즐겨찾기(C:\Users)에서 한 단계만 연다. 드라이브에서 갔다면 두 번이다.
        Assert.Equal([users], folders.EnumerateCalls);
        Assert.True(tree.Roots[0].IsFavorite);
        Assert.Equal(user, Selected(tree)?.Location);
    }

    // ── 되먹임을 끊는다 ──────────────────────────────────────────

    [Fact]
    public async Task Reveal_DoesNotAskThePaneToNavigate()
    {
        // 여기가 이 기능에서 가장 위험한 자리다. 노드를 고르면 IsSelected 세터가
        // NavigationRequested 를 쏘고 활성 페인이 그리로 간다 — 그 탐색이 다시 따라가기를
        // 부르면 페인↔트리가 서로를 영원히 민다.
        var (tree, work) = await DeepTreeAsync();
        var asked = 0;
        tree.NavigationRequested += (_, _) => asked++;

        await tree.RevealAsync(work, CancellationToken.None);

        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Reveal_LeavesTheNodeSelectableAfterwards()
    {
        // 되먹임을 끊는 플래그가 켜진 채로 남으면 그 뒤의 사용자 클릭이 조용히 무시된다.
        var (tree, work) = await DeepTreeAsync();
        await tree.RevealAsync(work, CancellationToken.None);
        var asked = 0;
        tree.NavigationRequested += (_, location) => asked++;

        tree.Roots[0].IsSelected = true;

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Reveal_MovingOn_LeavesOnlyOneNodeSelected()
    {
        // 선택이 둘이면 어느 것이 지금 자리인지 알 수 없다.
        var (tree, work) = await DeepTreeAsync();
        await tree.RevealAsync(work, CancellationToken.None);

        await tree.RevealAsync(Path(@"C:\Users"), CancellationToken.None);

        Assert.Single(tree.Roots.SelectMany(Walk), node => node.IsSelected);
        Assert.Equal(Path(@"C:\Users"), Selected(tree)?.Location);
    }

    [Fact]
    public async Task Reveal_WhenCancelled_DoesNotThrow()
    {
        // 폴더를 빠르게 옮기면 앞선 따라가기가 취소된다.
        var (tree, work) = await DeepTreeAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await tree.RevealAsync(work, cancelled.Token);

        Assert.Null(Selected(tree));
    }
}
