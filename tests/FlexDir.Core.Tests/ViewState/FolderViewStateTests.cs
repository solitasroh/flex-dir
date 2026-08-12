using FlexDir.Core.Locations;
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

    // ── 탭 (docs/PRD-v2.md §17) ────────────────────────────────────

    [Fact]
    public void TabState_KeepsFolderPinAndTitle()
    {
        var tab = new TabState(Folder(@"C:\Work"), IsPinned: true, Title: "일감");

        Assert.Equal(Folder(@"C:\Work"), tab.Folder);
        Assert.True(tab.IsPinned);
        Assert.Equal("일감", tab.Title);
    }

    [Fact]
    public void TabState_DefaultsToUnpinnedWithoutATitle()
    {
        // 제목이 없으면 폴더 이름을 쓴다 (docs/PRD-v2.md §17) — 그 판정은 ViewModel 이
        // 하므로 여기서는 "사용자가 정한 것이 없다" 만 표현한다.
        var tab = new TabState(Folder(@"C:\Work"));

        Assert.False(tab.IsPinned);
        Assert.Null(tab.Title);
    }

    [Fact]
    public void PaneTabsState_ComparesByValue()
    {
        // 왕복이 다른 인스턴스를 내므로 참조 비교로는 "바뀌었는가" 를 판정할 수 없다 —
        // FolderViewState.Equals 와 같은 자리다.
        Assert.Equal(
            new PaneTabsState([new TabState(Folder(@"C:\A")), new TabState(Folder(@"C:\B"))], 1),
            new PaneTabsState([new TabState(Folder(@"C:\A")), new TabState(Folder(@"C:\B"))], 1));
    }

    [Fact]
    public void PaneTabsState_CopiesTheListItWasGiven()
    {
        var tabs = new List<TabState> { new(Folder(@"C:\A")) };

        var state = new PaneTabsState(tabs);
        tabs.Clear();

        Assert.Single(state.Tabs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void PaneTabsState_WithAnActiveIndexOutsideTheList_ClampsIt(int index)
    {
        // 손상된 저장 파일이 활성 탭 번호를 벗어나게 적어도 페인이 빈 채로 뜨면 안 된다.
        // 던지지 않는 이유는 TreeWidth 와 같다 — 여기서 던지면 전역 상태 전체가 기본값으로
        // 접혀 창 배치까지 잃는다.
        var state = new PaneTabsState([new TabState(Folder(@"C:\A")), new TabState(Folder(@"C:\B"))], index);

        Assert.InRange(state.ActiveIndex, 0, 1);
    }

    [Fact]
    public void PaneTabsState_WithoutTabs_HasNoActiveIndex()
    {
        // 탭이 없는 페인은 저장 파일에서만 나온다. ViewModel 이 탭 하나를 만들어 채운다.
        var state = new PaneTabsState([], 3);

        Assert.Empty(state.Tabs);
        Assert.Equal(0, state.ActiveIndex);
    }

    [Fact]
    public void PaneTabsState_Single_IsOneUnpinnedTab()
    {
        // 구버전 저장 파일의 단수 필드가 이 모양으로 들어온다 (docs/PRD-v2.md §17).
        var state = PaneTabsState.Single(Folder(@"C:\Work"));

        Assert.Equal(new TabState(Folder(@"C:\Work")), Assert.Single(state.Tabs));
        Assert.Equal(0, state.ActiveIndex);
    }

    [Fact]
    public void GlobalDefault_HasNoPanes()
    {
        // 기억이 없는 것과 "탭 0개" 는 다르다 — 전자는 시작 폴더 규칙으로 가고 후자는
        // 손상된 파일이다.
        Assert.Null(GlobalViewState.Default.Panes);
    }

    [Fact]
    public void GlobalState_KeepsEveryPanesTabs()
    {
        var state = GlobalViewState.Default with
        {
            Panes =
            [
                new PaneState(new PaneTabsState([new TabState(Folder(@"C:\A")), new TabState(Folder(@"C:\B"), IsPinned: true)], 1)),
                new PaneState(PaneTabsState.Single(Folder(@"D:\"))),
            ],
        };

        Assert.Equal(2, state.Panes![0].Tabs.Tabs.Count);
        Assert.Equal(1, state.Panes[0].Tabs.ActiveIndex);
        Assert.True(state.Panes[0].Tabs.Tabs[1].IsPinned);
        Assert.Single(state.Panes[1].Tabs.Tabs);
    }

    [Fact]
    public void PaneState_DefaultsToDefaultColumns()
    {
        // 컬럼 폭은 페인마다 따로다 (사용자 지적 2026-08-10). 기억이 없는 페인은 기본 폭으로
        // 뜬다 — null 로 두면 읽는 쪽이 매번 기본값을 다시 고르게 된다.
        Assert.Equal(PaneColumns.Default, new PaneState(PaneTabsState.Single(Folder(@"C:\A"))).Columns);
    }

    [Fact]
    public void GlobalState_CopiesThePaneList()
    {
        // 호출자가 넘긴 리스트를 나중에 바꿔도 상태가 흔들리지 않는다 (PaneTabsState.Tabs 와
        // 같은 이유).
        var panes = new List<PaneState> { new(PaneTabsState.Single(Folder(@"C:\A"))) };
        var state = GlobalViewState.Default with { Panes = panes };

        panes.Add(new PaneState(PaneTabsState.Single(Folder(@"D:\"))));

        Assert.Single(state.Panes!);
    }

    // ── 분할 (docs/PRD-v2.md §18) ───────────────────────────────────

    [Fact]
    public void GlobalDefault_IsOnePane()
    {
        // 1분할이 기본화면이다 (사용자 결정 2026-08-12). 기억이 없는 첫 실행이 여기로 온다 —
        // 이미 쓰던 사람은 저장 파일이 2분할을 들고 있다 (JsonViewStateStore 의 마이그레이션).
        Assert.Equal(1, GlobalViewState.Default.PaneCount);
    }

    [Fact]
    public void GlobalDefault_SplitsRowsInHalf()
    {
        Assert.Equal(0.5, GlobalViewState.Default.RowRatio);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PaneCount_InsideRange_IsAccepted(int count)
    {
        Assert.Equal(count, (GlobalViewState.Default with { PaneCount = count }).PaneCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(5, 4)]
    [InlineData(int.MaxValue, 4)]
    public void PaneCount_OutsideRange_IsClamped(int stored, int expected)
    {
        // 비율과 달리 던지지 않는다. 5 에는 짐작할 수 있는 뜻이 있고(4), 그 하나 때문에
        // 전역 상태 전체가 기본값으로 접히면 창 배치와 마지막 폴더까지 잃는다
        // (PaneTabsState.ActiveIndex·TreeWidth 와 같은 판단).
        Assert.Equal(expected, (GlobalViewState.Default with { PaneCount = stored }).PaneCount);
    }

    [Fact]
    public void PaneCount_MayExceedTheRememberedPanes()
    {
        // 4분할을 처음 켜면 기억된 페인이 둘뿐이다. 모자란 자리는 ViewModel 이 활성 페인을
        // 복제해 채운다 (사용자 결정 2026-08-12) — 여기서 잘라내면 그 규칙이 닿을 곳이 없다.
        var state = GlobalViewState.Default with
        {
            Panes = [new PaneState(PaneTabsState.Single(Folder(@"C:\A")))],
            PaneCount = 4,
        };

        Assert.Equal(4, state.PaneCount);
        Assert.Single(state.Panes!);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public void RowRatio_InsideRange_IsAccepted(double ratio)
    {
        Assert.Equal(ratio, (GlobalViewState.Default with { RowRatio = ratio }).RowRatio);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RowRatio_OutsideRange_Throws(double ratio)
    {
        // 열 비율과 같은 취급이다. 비율은 손상됐을 때 짐작할 수 있는 뜻이 없고, 그대로
        // 통과시키면 페인이 사라진다.
        Assert.Throws<ArgumentOutOfRangeException>(() => GlobalViewState.Default with { RowRatio = ratio });
    }

    [Fact]
    public void MaxPanes_IsFour()
    {
        // 프리셋 네 단계다 (사용자 결정 2026-08-12). 이 수가 늘면 SplitLayout 의 슬롯 배치도
        // 함께 늘어야 한다 — 그쪽이 이 상수를 읽지 않고 자기 표를 든다.
        Assert.Equal(4, GlobalViewState.MaxPanes);
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _));

        return location;
    }
}
