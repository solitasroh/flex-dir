using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Operations;
using FlexDir.Core.Sorting;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

// System.Globalization 에도 SortKey 가 있다. 여기서 말하는 정렬 기준은 우리 것이다.
using SortKey = FlexDir.Core.Sorting.SortKey;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 좌/우 2분할과 활성 페인 (docs/PRD.md §2 · ADR-004).
/// <para>
/// 여기서 재는 것은 대부분 <b>일어나지 않아야 하는 일</b>이다 — 두 페인은 완전히 독립이고
/// (같은 폴더를 양쪽에 열어도 정렬·선택이 따로다, docs/PRD.md §4), 활성 전환은 선택을
/// 건드리지 않는다 (페인 간 복사의 출발점이 선택이다).
/// </para>
/// <para>
/// 저장소는 양쪽 페인과 워크스페이스가 하나를 공유한다 — 실제 조립도 그렇다. 폴더별 기억이
/// 공유돼도 이미 열린 페인의 상태가 서로 흔들리지 않아야 한다는 것이 독립성의 내용이다.
/// </para>
/// </summary>
public class WorkspaceViewModelTests
{
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
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly FakeDriveList drives = new();

    // ── 초기 상태 ─────────────────────────────────────────────────

    [Fact]
    public void New_AlwaysHasACrashNotice()
    {
        // Update·Tree·Settings 와 달리 선택이 아니다. Host/Program 이 여기에
        // DispatcherUnhandledException 을 거는데, 조립에 따라 없어질 수 있으면 그 배선이
        // 조용히 안 걸리고 앱은 예전처럼 예외 하나에 죽는다.
        var workspace = CreateWorkspace();

        Assert.NotNull(workspace.Crash);
        Assert.False(workspace.Crash.IsVisible);
    }

    [Fact]
    public void New_ActivatesTheLeftPane()
    {
        var workspace = CreateWorkspace();

        Assert.Same(workspace.LeftTabs(), workspace.ActivePane);
        Assert.Same(workspace.Left(), workspace.ActiveTab);
        Assert.Same(workspace.Right(), workspace.OtherTab);
        Assert.Equal(GlobalViewState.Default.SplitterRatio, workspace.SplitterRatio);
        Assert.Null(workspace.WindowPlacement);
    }

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkspaceViewModel(null!, viewStates));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceViewModel(CreatePane, null!));
    }

    // ── 활성 페인 ─────────────────────────────────────────────────

    [Fact]
    public void Activate_Right_SwapsTheActiveAndTheInactivePane()
    {
        var workspace = CreateWorkspace();

        workspace.ActivateCommand.Execute(workspace.RightTabs());

        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.Same(workspace.Right(), workspace.ActiveTab);
        Assert.Same(workspace.Left(), workspace.OtherTab);
    }

    [Fact]
    public void SwitchPane_Twice_ReturnsToTheOriginalPane()
    {
        var workspace = CreateWorkspace();

        workspace.SwitchPaneCommand.Execute(null);

        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.Same(workspace.Right(), workspace.ActiveTab);

        workspace.SwitchPaneCommand.Execute(null);

        Assert.Same(workspace.LeftTabs(), workspace.ActivePane);
        Assert.Same(workspace.Left(), workspace.ActiveTab);
    }

    [Fact]
    public void Activate_RaisesPropertyChangedForBothDerivedPanes()
    {
        // View 가 활성 표시를 여기에 바인딩한다 (docs/DESIGN.md §6).
        var workspace = CreateWorkspace();
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.ActivateCommand.Execute(workspace.RightTabs());

        Assert.Contains(nameof(WorkspaceViewModel.ActivePane), changed);
        Assert.Contains(nameof(WorkspaceViewModel.ActiveTab), changed);
        Assert.Contains(nameof(WorkspaceViewModel.OtherTab), changed);
    }

    [Fact]
    public void Activate_TheAlreadyActivePane_StaysQuiet()
    {
        var workspace = CreateWorkspace();
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.ActivateCommand.Execute(workspace.LeftTabs());

        Assert.Empty(changed);
    }

    [Fact]
    public async Task Activate_KeepsBothSelections()
    {
        // 전환하면 선택이 사라지는 2분할은 쓸 수 없다 — 페인 간 복사의 출발점이 선택이다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300), ("b.txt", 100));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300), ("p2.jpg", 100));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);
        workspace.Left().Selection.SelectSingle("a.txt");
        workspace.Right().Selection.SelectSingle("p2.jpg");

        workspace.ActivateCommand.Execute(workspace.RightTabs());
        workspace.SwitchPaneCommand.Execute(null);

        Assert.Equal(["a.txt"], workspace.Left().Selection.SelectedNames);
        Assert.Equal("a.txt", workspace.Left().Selection.Anchor);
        Assert.Equal(["p2.jpg"], workspace.Right().Selection.SelectedNames);
        Assert.Equal("p2.jpg", workspace.Right().Selection.Anchor);
    }

    // ── 마우스 보조 버튼의 뒤로·앞으로 (docs/PRD-v2.md §15) ───────

    [Fact]
    public async Task GoBackAt_ThePaneUnderThePointer_MovesThatPaneAndMakesItActive()
    {
        // 키보드(Alt+←)와 다른 점이 여기다 — 마우스에는 자리가 있다. 오른쪽 페인 위에서
        // 누르면 왼쪽이 활성이어도 오른쪽이 움직이고, 그 페인이 활성이 된다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Right().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);

        await workspace.GoBackAtAsync(workspace.Right());

        Assert.Equal(docs, workspace.Right().CurrentLocation);
        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.Null(workspace.Left().CurrentLocation);
    }

    [Fact]
    public async Task GoForwardAt_ThePaneUnderThePointer_MovesThatPane()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Right().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);
        await workspace.Right().GoBackAsync();

        await workspace.GoForwardAtAsync(workspace.Right());

        Assert.Equal(pics, workspace.Right().CurrentLocation);
        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
    }

    [Fact]
    public async Task GoBackAt_OutsideBothPanes_MovesTheActivePane()
    {
        // 트리·툴바 위에서 누른 것이 이것이다 (커서 아래 페인이 없다). 창 안에서 누른
        // 버튼이 아무 일도 하지 않으면 고장으로 보인다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Left().NavigateAsync(pics);

        await workspace.GoBackAtAsync(null);

        Assert.Equal(docs, workspace.Left().CurrentLocation);
        Assert.Same(workspace.LeftTabs(), workspace.ActivePane);
    }

    [Fact]
    public async Task GoBackAt_APaneWithNothingToGoBackTo_StillMakesItActive()
    {
        // 겨냥한 페인이 화면에 보이는 편이 낫다 — 아무 반응이 없으면 버튼이 죽은 것과
        // 구분되지 않는다. 갈 곳이 없는 것은 PaneHistory 가 조용히 판정한다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var workspace = CreateWorkspace();
        await workspace.Right().NavigateAsync(docs);

        await workspace.GoBackAtAsync(workspace.Right());

        Assert.Equal(docs, workspace.Right().CurrentLocation);
        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
    }

    // ── 두 페인의 독립 ────────────────────────────────────────────

    [Fact]
    public async Task TheSameFolderOnBothSides_SortsIndependently()
    {
        // docs/PRD.md §4 — 같은 폴더를 양쪽 페인에 열 수 있고 각각 독립적인 뷰 상태를 갖는다.
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(folder);
        await workspace.Right().NavigateAsync(folder);

        await workspace.Left().ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await workspace.Left().ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);

        Assert.Equal([new SortOrder(SortKey.Size)], workspace.Left().Sort);
        Assert.Equal(["b.txt", "c.txt", "a.txt"], workspace.Left().Items.Select(row => row.Name));

        Assert.Equal(FolderViewState.Default.Sort, workspace.Right().Sort);
        Assert.Equal(FolderViewState.Default.Mode, workspace.Right().ViewMode);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], workspace.Right().Items.Select(row => row.Name));
    }

    [Fact]
    public async Task TheSameFolderOnBothSides_SelectsIndependently()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(folder);
        await workspace.Right().NavigateAsync(folder);

        workspace.Right().Selection.SelectSingle("c.txt");
        workspace.Left().Selection.SelectSingle("a.txt");
        workspace.Left().Selection.Toggle("b.txt");

        Assert.Equal(2, workspace.Left().Selection.Count);
        Assert.True(workspace.Left().Selection.IsSelected("a.txt"));
        Assert.True(workspace.Left().Selection.IsSelected("b.txt"));

        Assert.Equal(["c.txt"], workspace.Right().Selection.SelectedNames);
        Assert.Equal("c.txt", workspace.Right().Selection.Anchor);
    }

    [Fact]
    public async Task NavigatingOnePane_LeavesTheOtherWhereItWas()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(docs);

        await workspace.Left().NavigateAsync(pics);

        Assert.Equal(pics, workspace.Left().CurrentLocation);
        Assert.True(workspace.Left().CanGoBack);

        // 히스토리도 페인별이다 (docs/PRD.md §2).
        Assert.Equal(docs, workspace.Right().CurrentLocation);
        Assert.False(workspace.Right().CanGoBack);
        Assert.Equal(["a.txt"], workspace.Right().Items.Select(row => row.Name));
    }

    // ── 스플리터 비율 ─────────────────────────────────────────────

    [Theory]
    [InlineData(0.01, 0.15)]
    [InlineData(0.0, 0.15)]
    [InlineData(-3.0, 0.15)]
    [InlineData(0.15, 0.15)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.85, 0.85)]
    [InlineData(0.99, 0.85)]
    [InlineData(12.0, 0.85)]
    public void SplitterRatio_IsClampedToTheAllowedRange(double assigned, double expected)
    {
        // 예외를 던지지 않는다 — 저장 파일이 손상돼도 페인이 사라지지만 않으면 된다.
        var workspace = CreateWorkspace();

        workspace.SplitterRatio = assigned;

        Assert.Equal(expected, workspace.SplitterRatio);
    }

    [Fact]
    public void SplitterRatio_NaN_StaysInsideTheAllowedRange()
    {
        // NaN 은 모든 관계 비교가 false 라서 순진한 클램프를 그대로 통과한다.
        var workspace = CreateWorkspace();

        workspace.SplitterRatio = double.NaN;

        Assert.InRange(workspace.SplitterRatio, 0.15, 0.85);
    }

    [Fact]
    public void SplitterRatio_RaisesPropertyChangedOnlyWhenTheClampedValueChanges()
    {
        var workspace = CreateWorkspace();
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.SplitterRatio = 0.99;
        workspace.SplitterRatio = 12.0;

        // 둘 다 같은 경계로 잘렸으므로 알림은 한 번이다.
        Assert.Equal([nameof(WorkspaceViewModel.SplitterRatio)], changed);
    }

    // ── 전역 상태 복원·저장 ───────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_AppliesTheSavedGlobalState()
    {
        var placement = new WindowPlacement(120, 80, 1400, 900, Maximized: false);
        await viewStates.SaveGlobalAsync(new GlobalViewState(0.35, placement), CancellationToken.None);
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(0.35, workspace.SplitterRatio);
        Assert.Equal(placement, workspace.WindowPlacement);
    }

    [Fact]
    public async Task RestoreAsync_ClampsARatioStoredOutsideTheAllowedRange()
    {
        // docs/DESIGN.md §1 — 창 최소 너비 900 에서 페인 최소 너비 320 을 지키려면
        // 비율이 양 끝으로 가서는 안 된다.
        await viewStates.SaveGlobalAsync(new GlobalViewState(0.02, null), CancellationToken.None);
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(0.15, workspace.SplitterRatio);
    }

    [Fact]
    public async Task RestoreAsync_WithAFailingStore_UsesTheDefault()
    {
        // 전역 뷰 상태는 캐시다 (CLAUDE.md §4). 읽지 못해도 창은 떠야 한다.
        var workspace = new WorkspaceViewModel(CreatePane, new FailingViewStateStore());

        await workspace.RestoreAsync(null);

        Assert.Equal(GlobalViewState.Default.SplitterRatio, workspace.SplitterRatio);
        Assert.Null(workspace.WindowPlacement);
    }

    [Fact]
    public async Task PersistAsync_SavesTheCurrentGlobalState()
    {
        var workspace = CreateWorkspace();
        workspace.SplitterRatio = 0.4;
        workspace.WindowPlacement = new WindowPlacement(0, 0, 1280, 800, Maximized: true);

        await workspace.PersistAsync();

        var saved = await viewStates.LoadGlobalAsync(CancellationToken.None);

        // 아무 폴더도 열지 않은 페인은 담을 것이 없다 — 그러면 컬럼 폭도 함께 빠진다
        // (docs/PRD-v2.md §18: 컬럼 폭이 PaneState 에 붙어 탭 목록과 한 묶음이 됐다).
        // "기억이 없다" 로 저장되는 편이 옳다: 다음 실행은 시작 폴더 규칙으로 가야 한다.
        // 분할 수는 담긴다 — 다음 실행이 쓰던 화면으로 돌아오는 근거다 (docs/PRD-v2.md §18).
        Assert.Equal(new GlobalViewState(0.4, workspace.WindowPlacement) { PaneCount = 2 }, saved);
    }

    [Fact]
    public async Task PersistAsync_WithAFailingStore_DoesNotThrow()
    {
        // 저장 실패가 창을 닫는 길을 막으면 안 된다.
        var workspace = new WorkspaceViewModel(CreatePane, new FailingViewStateStore());
        workspace.SplitterRatio = 0.3;

        await workspace.PersistAsync();

        Assert.Equal(0.3, workspace.SplitterRatio);
    }

    // ── 반대편 폴더 열기 ──────────────────────────────────────────

    [Fact]
    public async Task OpenOtherPaneLocation_OpensTheOtherFolderInTheActivePane()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(pics, workspace.Left().CurrentLocation);
        Assert.Equal(["p1.jpg"], workspace.Left().Items.Select(row => row.Name));

        // 히스토리에 남는다 — 뒤로가 원래 폴더로 돌아간다.
        Assert.True(workspace.Left().CanGoBack);

        // 반대편은 움직이지 않는다.
        Assert.Equal(pics, workspace.Right().CurrentLocation);
    }

    [Fact]
    public async Task OpenOtherPaneLocation_FollowsTheActivePane()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);
        workspace.ActivateCommand.Execute(workspace.RightTabs());

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(docs, workspace.Right().CurrentLocation);
        Assert.Equal(docs, workspace.Left().CurrentLocation);
    }

    [Fact]
    public async Task OpenOtherPaneLocation_WithNothingOpenOnTheOtherSide_DoesNothing()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(docs);

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(docs, workspace.Left().CurrentLocation);
        Assert.Equal(PaneStatus.Idle, workspace.Left().Status);
        Assert.Null(workspace.Right().CurrentLocation);

        // 열거를 다시 하지도 않는다.
        Assert.Single(source.EnumerateCalls);
    }

    // ── Details 컬럼 폭 (사용자 요청 2026-08-10) ──────────────────

    [Fact]
    public async Task RestoreAsync_BringsBackEachPaneColumnWidths()
    {
        // 컬럼 폭은 이제 탭 목록과 한 묶음이다 (PaneState) — 페인이 기억되어야 폭도 있다.
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default.WithTwoPanes(
                PaneTabsState.Single(Folder(@"C:\A")),
                PaneTabsState.Single(Folder(@"C:\B")),
                new PaneColumns(400, 70, 200, 180),
                new PaneColumns(200, 60, 90, 100)),
            CancellationToken.None);
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(new PaneColumns(400, 70, 200, 180), workspace.Left().Columns);
        Assert.Equal(new PaneColumns(200, 60, 90, 100), workspace.Right().Columns);
    }

    [Fact]
    public async Task RestoreAsync_WithoutColumns_UsesTheDefaults()
    {
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(PaneColumns.Default, workspace.Left().Columns);
    }

    [Fact]
    public async Task PersistAsync_SavesEachPaneSeparately()
    {
        // 폴더를 연다 — 컬럼 폭은 탭 목록과 한 묶음이라 (PaneState) 담을 탭이 있어야 함께
        // 실린다. 아무 곳도 열지 않은 페인은 "기억이 없다" 로 저장되는 것이 옳다.
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(Folder(@"C:\A", ("a.txt", 1)));
        await workspace.Right().NavigateAsync(Folder(@"C:\B", ("b.txt", 1)));
        workspace.Left().TypeColumnWidth = 200;

        await workspace.PersistAsync();

        var saved = await viewStates.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(200, saved.ColumnsAt(0)?.Type);

        // 반대편은 건드리지 않는다 — 한쪽에서 끌 때 다른 쪽이 따라 움직이면 안 된다.
        Assert.Equal(PaneColumns.Default.Type, saved.ColumnsAt(1)?.Type);
    }

    [Fact]
    public void ColumnWidth_OnOnePane_LeavesTheOtherAlone()
    {
        var workspace = CreateWorkspace();

        workspace.Left().NameColumnWidth = 500;

        Assert.Equal(PaneColumns.Default.Name, workspace.Right().NameColumnWidth);
    }

    // ── 폴더 트리 (docs/PRD-v2.md §10 · 사용자 결정 2026-08-10) ────

    [Fact]
    public async Task RestoreAsync_BringsBackTheTreeShape()
    {
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default with { TreeVisible = false, TreeWidth = 300 }, CancellationToken.None);
        var (workspace, tree) = CreateWorkspaceWithTree();

        await workspace.RestoreAsync(null);

        Assert.False(tree.IsVisible);
        Assert.Equal(300, tree.Width);
    }

    [Fact]
    public async Task RestoreAsync_FillsTheTreeRoots()
    {
        // 드라이브 열거는 저장소에 닿는다 — 복원과 같은 자리에서 한 번에 한다.
        drives.Drives.Add(new DriveEntry(Loc(@"C:\"), "로컬 디스크 (C:)", null));
        var (workspace, tree) = CreateWorkspaceWithTree();

        await workspace.RestoreAsync(null);

        Assert.Single(tree.Roots);
    }

    [Fact]
    public async Task PersistAsync_SavesTheTreeShape()
    {
        var (workspace, tree) = CreateWorkspaceWithTree();
        tree.IsVisible = false;
        tree.Width = 300;

        await workspace.PersistAsync();

        var saved = await viewStates.LoadGlobalAsync(CancellationToken.None);

        Assert.False(saved.TreeVisible);
        Assert.Equal(300, saved.TreeWidth);
    }

    [Fact]
    public async Task PersistAsync_WithoutATree_KeepsTheDefaults()
    {
        // 트리 없이 조립된 워크스페이스도 저장은 해야 한다 — 업데이트 알림과 같은 자리다.
        await CreateWorkspace().PersistAsync();

        var saved = await viewStates.LoadGlobalAsync(CancellationToken.None);

        Assert.True(saved.TreeVisible);
        Assert.Equal(GlobalViewState.DefaultTreeWidth, saved.TreeWidth);
    }

    [Fact]
    public void ToggleTree_FlipsVisibility()
    {
        var (workspace, tree) = CreateWorkspaceWithTree();

        workspace.ToggleTreeCommand.Execute(null);

        Assert.False(tree.IsVisible);

        workspace.ToggleTreeCommand.Execute(null);

        Assert.True(tree.IsVisible);
    }

    [Fact]
    public void ToggleTree_WithoutATree_DoesNothing()
    {
        // 툴바 버튼은 트리 없이 조립돼도 눌린다. 눌러서 터지면 안 된다.
        CreateWorkspace().ToggleTreeCommand.Execute(null);
    }

    [Fact]
    public async Task PinCurrentFolder_PinsWhatTheActivePaneIsShowing()
    {
        // 툴바 버튼의 자리다 — 지금 보고 있는 폴더를 한 번에 고정한다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.Left().NavigateAsync(docs);

        await workspace.PinCurrentFolderCommand.ExecuteAsync(null);

        Assert.Equal([docs], tree.Roots.Where(node => node.IsFavorite).Select(node => node.Location));
    }

    [Fact]
    public async Task PinCurrentFolder_FollowsTheActivePane()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        var pics = Folder(@"C:\Temp\Pics", ("p.jpg", 100));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.Left().NavigateAsync(docs);
        await workspace.Right().NavigateAsync(pics);
        workspace.ActivateCommand.Execute(workspace.RightTabs());

        await workspace.PinCurrentFolderCommand.ExecuteAsync(null);

        Assert.Equal([pics], tree.Roots.Where(node => node.IsFavorite).Select(node => node.Location));
    }

    [Fact]
    public async Task PinCurrentFolder_WithNothingOpen_DoesNothing()
    {
        var (workspace, tree) = CreateWorkspaceWithTree();

        await workspace.PinCurrentFolderCommand.ExecuteAsync(null);

        Assert.Empty(tree.Roots);
    }

    [Fact]
    public async Task PinCurrentFolder_WithoutATree_DoesNothing()
    {
        // 툴바 버튼은 트리 없이 조립돼도 눌린다.
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(Folder(@"C:\Temp\Docs", ("a.txt", 100)));

        await workspace.PinCurrentFolderCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ContextMenuPin_PinsTheSelectedFolders()
    {
        // 목록 우클릭 → '즐겨찾기에 추가' (docs/PRD-v2.md §10-2). shell 메뉴 맨 위에 우리
        // 항목이 붙고, 고르면 shell 이 아니라 우리가 처리한다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        var sub = Subfolder(docs, "Sub");
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.Left().NavigateAsync(docs);
        workspace.Left().Selection.SelectSingle("Sub");
        contextMenus.ChosenAppCommand = 0;

        await workspace.Left().ShowContextMenuCommand.ExecuteAsync(new ScreenPoint(10, 10));

        Assert.Equal([sub], tree.Roots.Where(node => node.IsFavorite).Select(node => node.Location));

        // 우리 항목이 메뉴에 실제로 실렸는지도 본다 — 실리지 않으면 고를 수가 없다.
        Assert.Equal(["즐겨찾기에 추가"], contextMenus.Requests[0].AppCommands);
    }

    [Fact]
    public async Task ContextMenuPin_OnEmptySpace_PinsTheFolderItself()
    {
        // 선택이 비어 있으면 배경 메뉴다 — 지금 보고 있는 폴더를 고정한다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        Subfolder(Loc(@"C:\Temp"), "Docs");
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.Left().NavigateAsync(docs);
        contextMenus.ChosenAppCommand = 0;

        await workspace.Left().ShowContextMenuCommand.ExecuteAsync(new ScreenPoint(10, 10));

        Assert.Equal([docs], tree.Roots.Where(node => node.IsFavorite).Select(node => node.Location));
    }

    [Fact]
    public async Task ContextMenu_WhenAShellVerbIsChosen_PinsNothing()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.Left().NavigateAsync(docs);
        contextMenus.ChosenAppCommand = null;

        await workspace.Left().ShowContextMenuCommand.ExecuteAsync(new ScreenPoint(10, 10));

        Assert.DoesNotContain(tree.Roots, node => node.IsFavorite);
    }

    [Fact]
    public async Task TreeSelection_OpensInTheActivePane()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        drives.Drives.Add(new DriveEntry(docs, "Docs", null));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.RestoreAsync(null);

        tree.Roots[0].IsSelected = true;

        Assert.Equal(docs, workspace.Left().CurrentLocation);
        Assert.Null(workspace.Right().CurrentLocation);
    }

    [Fact]
    public async Task TreeSelection_FollowsTheActivePane()
    {
        // 트리는 창에 하나뿐이고 양쪽을 다 몰 수 있어야 한다 (사용자 결정 2026-08-10) —
        // Tab 으로 옮긴 뒤 고르면 그쪽이 간다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        drives.Drives.Add(new DriveEntry(docs, "Docs", null));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.RestoreAsync(null);
        workspace.ActivateCommand.Execute(workspace.RightTabs());

        tree.Roots[0].IsSelected = true;

        Assert.Equal(docs, workspace.Right().CurrentLocation);
        Assert.Null(workspace.Left().CurrentLocation);
    }

    [Fact]
    public async Task PaneNavigation_DoesNotMoveTheFolderTree()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 100));
        var elsewhere = Folder(@"C:\Temp\Elsewhere", ("b.txt", 100));
        drives.Drives.Add(new DriveEntry(docs, "Docs", null));
        var (workspace, tree) = CreateWorkspaceWithTree();
        await workspace.RestoreAsync(null);

        tree.Roots[0].IsSelected = true;
        Assert.Equal(docs, workspace.Left().CurrentLocation);

        await workspace.Left().NavigateAsync(elsewhere);

        Assert.True(tree.Roots[0].IsSelected);
        Assert.Equal(elsewhere, workspace.Left().CurrentLocation);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    // ── 시작 폴더 복원 (phase B-2) ────────────────────────────────

    [Fact]
    public async Task RestoreAsync_OpensTheLastFoldersOfBothPanes()
    {
        var left = Folder(@"C:\Temp\Left", ("a.txt", 100));
        var right = Folder(@"C:\Temp\Right", ("b.txt", 100));
        var fallback = Folder(@"C:\Users\Me", ("c.txt", 100));
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default.WithTwoPanes(PaneTabsState.Single(left), PaneTabsState.Single(right)),
            CancellationToken.None);
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(fallback);

        Assert.Equal(left, workspace.Left().CurrentLocation);
        Assert.Equal(right, workspace.Right().CurrentLocation);
    }

    [Fact]
    public async Task RestoreAsync_WithoutMemory_FallsBackToTheGivenFolder()
    {
        // 처음 실행이거나 옛 파일이다 — 빈 페인 대신 폴백(사용자 프로필)을 연다.
        var fallback = Folder(@"C:\Users\Me", ("c.txt", 100));
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(fallback);

        Assert.Equal(fallback, workspace.Left().CurrentLocation);
        Assert.Equal(fallback, workspace.Right().CurrentLocation);
    }

    [Fact]
    public async Task RestoreAsync_WithNoFallbackEither_LeavesThePanesEmpty()
    {
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Null(workspace.Left().CurrentLocation);
        Assert.Null(workspace.Right().CurrentLocation);
    }

    [Fact]
    public async Task PersistAsync_SavesTheCurrentFoldersOfBothPanes()
    {
        var left = Folder(@"C:\Temp\Left", ("a.txt", 100));
        var right = Folder(@"C:\Temp\Right", ("b.txt", 100));
        var workspace = CreateWorkspace();
        await workspace.Left().NavigateAsync(left);
        await workspace.Right().NavigateAsync(right);

        await workspace.PersistAsync();

        var state = await viewStates.LoadGlobalAsync(CancellationToken.None);
        Assert.Equal(left, Assert.Single(state.TabsAt(0)!.Tabs).Folder);
        Assert.Equal(right, Assert.Single(state.TabsAt(1)!.Tabs).Folder);
    }

    // ── 클릭에 의한 활성 전환 ─────────────────────────────────────

    [Fact]
    public async Task ItemClickInTheInactivePane_MakesThatPaneActive()
    {
        // 비활성 페인의 항목 클릭은 선택과 활성 전환이 한 동작이다 (목업 동작 그대로).
        // View 가 페인 전환을 따로 쏘면 클릭 한 번에 바인딩 두 개가 경합한다.
        var folder = Folder(@"C:\Temp", ("a.txt", 100));
        var workspace = CreateWorkspace();
        await workspace.Right().NavigateAsync(folder);

        workspace.Right().SelectItemCommand.Execute(workspace.Right().Items[0]);

        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.Equal(["a.txt"], workspace.Right().Selection.SelectedNames);
    }

    private WorkspaceViewModel CreateWorkspace() => new WorkspaceViewModel(CreatePane, viewStates).Split2();

    /// <summary>트리를 물린 워크스페이스. 트리를 보는 테스트만 이것을 쓴다.</summary>
    private (WorkspaceViewModel Workspace, FolderTreeViewModel Tree) CreateWorkspaceWithTree()
    {
        var tree = new FolderTreeViewModel(
            drives, new FakeNetworkPlaceList(), new FakeFavoriteStore(), source, dispatcher);

        return (new WorkspaceViewModel(CreatePane, viewStates, update: null, tree).Split2(), tree);
    }

    /// <summary>
    /// 부모 폴더의 <b>자식으로</b> 폴더 항목을 등록한다. 즐겨찾기의 존재 확인이
    /// <c>TryGetItemAsync</c> 로 그것을 찾으므로, 폴더를 열어 두는 것만으로는 부족하다.
    /// </summary>
    private LocationId Subfolder(LocationId parent, string name)
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

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>크기를 함께 준다 — 이름 순서와 크기 순서가 달라야 정렬 독립이 보인다.</summary>
    private LocationId Folder(string path, params (string Name, long Size)[] entries)
    {
        var folder = Loc(path);

        source.Folders[folder] =
        [
            .. entries.Select(entry => new FileItem(
                entry.Name,
                folder.Combine(entry.Name),
                entry.Size,
                DateTimeOffset.UnixEpoch,
                FileItemFlags.None)),
        ];

        return folder;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    /// <summary>전역 상태를 읽지도 쓰지도 못하는 저장소. 설정 파일이 손상됐거나 잠긴 상황이다.</summary>
    private sealed class FailingViewStateStore : IViewStateStore
    {
        public ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct)
            => throw new IOException("뷰 상태를 읽을 수 없다.");

        public ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct)
            => throw new IOException("뷰 상태를 쓸 수 없다.");

        public ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
            => throw new IOException("전역 상태를 읽을 수 없다.");

        public ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct)
            => throw new IOException("전역 상태를 쓸 수 없다.");
    }
}
