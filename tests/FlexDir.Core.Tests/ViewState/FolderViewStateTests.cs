using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.Core.Tests.ViewState;

/// <summary>
/// 폴더별 뷰 상태와 전역 상태의 값 규칙 (docs/PRD.md §2 폴더별 기억 · docs/ARCHITECTURE.md §4).
/// 뷰 모드 네 종은 docs/DESIGN.md §2 의 Details · 목록 · 타일 · 큰 아이콘 과 1:1 이다.
/// </summary>
public class FolderViewStateTests
{
    // ── 기본값 ──────────────────────────────────────────────────────
    // 기억이 없는 폴더가 정상 상황이므로 기본값이 한 군데에만 있어야 한다.

    [Fact]
    public void Default_IsDetailsWithNameAscending()
    {
        var state = FolderViewState.Default;

        Assert.Equal(ViewMode.Details, state.Mode);
        Assert.Equal([new SortOrder(SortKey.Name)], state.Sort);
    }

    [Fact]
    public void Default_SortIsAscending()
    {
        Assert.False(Assert.Single(FolderViewState.Default.Sort).Descending);
    }

    [Fact]
    public void Default_IsAcceptedByFileItemComparer()
    {
        // 정렬 키를 그대로 비교기에 넘기는 것이 이 타입의 존재 이유다.
        _ = new FileItemComparer(FolderViewState.Default.Sort);
    }

    // ── 정렬 키 검사 ────────────────────────────────────────────────
    // 빈 키를 통과시키면 FileItemComparer 가 훨씬 나중에 터진다 — 저장 파일이
    // 손상된 경우 원인 지점과 증상 지점이 멀어진다.

    [Fact]
    public void EmptySort_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FolderViewState(ViewMode.List, []));
    }

    [Fact]
    public void NullSort_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderViewState(ViewMode.List, null!));
    }

    [Fact]
    public void WithEmptySort_Throws()
    {
        // 'with' 로 만든 사본도 같은 검사를 받는다.
        Assert.Throws<ArgumentException>(() => FolderViewState.Default with { Sort = [] });
    }

    [Fact]
    public void WithMode_KeepsSort()
    {
        var state = FolderViewState.Default with { Mode = ViewMode.LargeIcons };

        Assert.Equal(ViewMode.LargeIcons, state.Mode);
        Assert.Equal(FolderViewState.Default.Sort, state.Sort);
    }

    // ── 방어 복사 ───────────────────────────────────────────────────

    [Fact]
    public void MutatingTheSourceList_DoesNotChangeTheState()
    {
        var keys = new List<SortOrder> { new(SortKey.Size, Descending: true) };

        var state = new FolderViewState(ViewMode.Tiles, keys);
        keys.Clear();

        Assert.Equal([new SortOrder(SortKey.Size, Descending: true)], state.Sort);
    }

    // ── 동등성 ──────────────────────────────────────────────────────
    // 저장·복원 왕복은 다른 리스트 인스턴스를 낸다. 참조 비교로는
    // "바뀌었는가" 를 판정할 수 없다.

    [Fact]
    public void SameContentInDifferentListInstances_AreEqual()
    {
        var a = new FolderViewState(ViewMode.Tiles, [new SortOrder(SortKey.Modified, Descending: true)]);
        var b = new FolderViewState(ViewMode.Tiles, [new SortOrder(SortKey.Modified, Descending: true)]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentSortDirection_IsNotEqual()
    {
        var ascending = new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Name)]);
        var descending = new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Name, Descending: true)]);

        Assert.NotEqual(ascending, descending);
    }

    [Fact]
    public void DifferentSortKeyOrder_IsNotEqual()
    {
        var typeFirst = new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Type), new SortOrder(SortKey.Name)]);
        var nameFirst = new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Name), new SortOrder(SortKey.Type)]);

        Assert.NotEqual(typeFirst, nameFirst);
    }

    [Fact]
    public void DifferentMode_IsNotEqual()
    {
        Assert.NotEqual(FolderViewState.Default, FolderViewState.Default with { Mode = ViewMode.List });
    }

    // ── 그룹화 (docs/PRD-v2.md §6-1) ────────────────────────────────
    // 꺼진 것이 기본이다. 그룹화가 꺼져 있으면 목록 경로가 v1 과 같아야 한다.

    [Fact]
    public void Default_HasNoGrouping()
    {
        Assert.Null(FolderViewState.Default.GroupBy);
        Assert.Empty(FolderViewState.Default.Collapsed);
    }

    [Fact]
    public void WithGroupBy_KeepsModeAndSort()
    {
        var state = FolderViewState.Default with { GroupBy = SortKey.Type };

        Assert.Equal(SortKey.Type, state.GroupBy);
        Assert.Equal(ViewMode.Details, state.Mode);
        Assert.Equal([new SortOrder(SortKey.Name)], state.Sort);
    }

    [Fact]
    public void DifferentGroupBy_IsNotEqual()
    {
        Assert.NotEqual(FolderViewState.Default, FolderViewState.Default with { GroupBy = SortKey.Size });
    }

    // 접힌 그룹은 집합이다 — 저장·복원 왕복이 순서를 뒤집어도 "바뀌었다" 가 되면
    // 폴더를 떠날 때마다 쓸데없이 다시 쓴다.
    [Fact]
    public void CollapsedGroups_CompareAsASetNotAList()
    {
        var a = FolderViewState.Default with { Collapsed = ["ㄴ", "ㄱ"] };
        var b = FolderViewState.Default with { Collapsed = ["ㄱ", "ㄴ"] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void CollapsedGroups_DropDuplicates()
    {
        var state = FolderViewState.Default with { Collapsed = ["ㄱ", "ㄱ"] };

        Assert.Equal(["ㄱ"], state.Collapsed);
    }

    // 정렬 키와 달리 비어 있는 것이 정상이므로 던지지 않는다.
    [Fact]
    public void CollapsedGroups_NullBecomesEmpty()
    {
        Assert.Empty(new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Name)], null, null).Collapsed);
    }

    [Fact]
    public void MutatingTheCollapsedSourceList_DoesNotChangeTheState()
    {
        var collapsed = new List<string> { "ㄱ" };

        var state = FolderViewState.Default with { Collapsed = collapsed };
        collapsed.Clear();

        Assert.Equal(["ㄱ"], state.Collapsed);
    }

    // ── 전역 상태 ───────────────────────────────────────────────────

    [Fact]
    public void GlobalDefault_IsHalfSplitWithoutWindowPlacement()
    {
        Assert.Equal(0.5, GlobalViewState.Default.SplitterRatio);
        Assert.Null(GlobalViewState.Default.Window);
    }

    [Fact]
    public void GlobalDefault_ShowsTheTree()
    {
        // 처음 켠 사람에게 트리가 있다는 것을 알릴 길이 이것뿐이다 — 토글은 트리를 보고
        // 나서야 찾는다.
        Assert.True(GlobalViewState.Default.TreeVisible);
        Assert.Equal(GlobalViewState.DefaultTreeWidth, GlobalViewState.Default.TreeWidth);
    }

    [Fact]
    public void GlobalState_KeepsTheTreeShape()
    {
        var state = GlobalViewState.Default with { TreeVisible = false, TreeWidth = 300 };

        Assert.False(state.TreeVisible);
        Assert.Equal(300, state.TreeWidth);
    }

    [Fact]
    public void GlobalState_WithAnAbsurdTreeWidth_DoesNotThrow()
    {
        // 스플리터 비율과 다르다. 저장 파일이 손상돼 -3 이 들어와도 여기서 던지면 전역
        // 상태 전체가 기본값으로 접혀 창 배치와 마지막 폴더까지 잃는다 — 폭은 트리가
        // 자르면 되는 값이다 (FolderTreeViewModel.Width).
        Assert.Equal(-3, (GlobalViewState.Default with { TreeWidth = -3 }).TreeWidth);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public void SplitterRatio_InsideRange_IsAccepted(double ratio)
    {
        Assert.Equal(ratio, new GlobalViewState(ratio, null).SplitterRatio);
    }

    [Theory]
    [InlineData(-3)]          // 손상된 저장 파일. 그냥 통과시키면 페인이 사라진다
    [InlineData(0)]           // 좌 페인 폭 0
    [InlineData(1)]           // 우 페인 폭 0
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SplitterRatio_OutsideRange_Throws(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlobalViewState(ratio, null));
    }

    [Fact]
    public void WithSplitterRatio_OutsideRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GlobalViewState.Default with { SplitterRatio = -3 });
    }

    [Fact]
    public void GlobalState_KeepsWindowPlacement()
    {
        var placement = new WindowPlacement(100, 200, 1280, 800, Maximized: false);

        var state = new GlobalViewState(0.4, placement);

        Assert.Equal(0.4, state.SplitterRatio);
        Assert.Equal(placement, state.Window);
    }

    [Fact]
    public void WindowPlacement_ComparesByValue()
    {
        Assert.Equal(
            new WindowPlacement(0, 0, 1280, 800, Maximized: true),
            new WindowPlacement(0, 0, 1280, 800, Maximized: true));
    }
}
