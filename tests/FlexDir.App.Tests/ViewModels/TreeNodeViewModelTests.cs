using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 트리의 노드 하나. 자식을 언제 얻어 오는지와 선택이 무엇을 부르는지만 안다 —
/// 실제 열거는 <see cref="FolderTreeViewModel"/> 이 한다.
/// <para>
/// 노드가 소스를 직접 들지 않는 이유: 그러면 노드 테스트가 열거 fake 를 요구하고,
/// 트리와 노드가 같은 지식을 두 벌 갖게 된다.
/// </para>
/// </summary>
public class TreeNodeViewModelTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    private static TreeNodeViewModel Node(
        Func<TreeNodeViewModel, Task>? expand = null,
        Action<TreeNodeViewModel>? select = null)
        => new(Path(@"C:\Temp"), "Temp", expand, select);

    [Fact]
    public void CanExpand_BeforeAnythingIsLoaded_IsTrue()
    {
        // 낙관적이다. 하위 폴더가 있는지 미리 알려면 그 폴더를 열어 봐야 하고, 트리에 선
        // 모든 노드에 대해 그것을 하면 저장소 호출이 폭발한다 (CLAUDE.md §3).
        Assert.True(Node().CanExpand);
    }

    [Fact]
    public void IsNetwork_ForAUncPath_IsTrueWithoutBeingTold()
    {
        Assert.True(new TreeNodeViewModel(Path(@"\\10.10.10.23\home"), "home").IsNetwork);
    }

    [Fact]
    public void IsNetwork_ForAMappedDrive_IsTrueOnlyWhenTold()
    {
        // Z:\ 는 경로만 보면 로컬이다. 매핑됐다는 것은 드라이브 목록만 안다
        // (DriveEntry.Server) — 그래서 만드는 쪽이 실어 준다.
        Assert.False(new TreeNodeViewModel(Path(@"Z:\"), "nas (Z:)").IsNetwork);
        Assert.True(new TreeNodeViewModel(Path(@"Z:\"), "nas (Z:)", isNetwork: true).IsNetwork);
    }

    [Fact]
    public void IsExpanded_SetToTrue_AsksForChildren()
    {
        var asked = 0;
        var node = Node(expand: _ => { asked++; return Task.CompletedTask; });

        node.IsExpanded = true;

        Assert.Equal(1, asked);
    }

    [Fact]
    public void IsExpanded_SetToFalse_AsksForNothing()
    {
        var asked = 0;
        var node = Node(expand: _ => { asked++; return Task.CompletedTask; });

        node.IsExpanded = false;

        Assert.Equal(0, asked);
    }

    [Fact]
    public void IsExpanded_AfterTheChildrenArrived_DoesNotAskAgain()
    {
        // 접었다 펴는 것은 흔한 동작이다. 매번 다시 열거하면 네트워크 폴더에서 그때마다
        // 초 단위로 멈춘다 — 갱신은 감시가 할 일이지 펼치기가 할 일이 아니다.
        var asked = 0;
        var node = Node(expand: _ => { asked++; return Task.CompletedTask; });

        node.IsExpanded = true;
        node.Realize([]);
        node.IsExpanded = false;
        node.IsExpanded = true;

        Assert.Equal(1, asked);
    }

    [Fact]
    public void IsSelected_SetToTrue_Selects()
    {
        TreeNodeViewModel? selected = null;
        var node = Node(select: which => selected = which);

        node.IsSelected = true;

        Assert.Same(node, selected);
    }

    [Fact]
    public void IsSelected_SetToFalse_SelectsNothing()
    {
        // TreeView 는 선택이 옮겨갈 때 이전 노드를 false 로 되돌린다. 그것이 탐색을
        // 일으키면 폴더를 하나 고를 때마다 페인이 두 번 움직인다.
        var selections = 0;
        var node = Node(select: _ => selections++);

        node.IsSelected = true;
        node.IsSelected = false;

        Assert.Equal(1, selections);
    }

    [Fact]
    public void Realize_ReplacesTheChildren()
    {
        var node = Node();
        var child = new TreeNodeViewModel(Path(@"C:\Temp\Docs"), "Docs");

        node.Realize([child]);

        Assert.Equal([child], node.Children);
        Assert.True(node.CanExpand);
    }

    [Fact]
    public void Realize_WithNothingInside_TurnsOffCanExpand()
    {
        // 열어 봤더니 하위 폴더가 없었다. 화살표를 남기면 눌러도 아무 일이 없다.
        var node = Node();

        node.Realize([]);

        Assert.False(node.CanExpand);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Realize_Twice_DoesNotAccumulate()
    {
        // 새로 고침이 같은 노드를 다시 채울 수 있다. 더하면 같은 폴더가 두 번 선다.
        var node = Node();

        node.Realize([new TreeNodeViewModel(Path(@"C:\Temp\A"), "A")]);
        node.Realize([new TreeNodeViewModel(Path(@"C:\Temp\B"), "B")]);

        Assert.Equal(["B"], node.Children.Select(child => child.Label));
    }
}
