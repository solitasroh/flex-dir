using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Sorting;
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
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 초기 상태 ─────────────────────────────────────────────────

    [Fact]
    public void New_ActivatesTheLeftPane()
    {
        var workspace = CreateWorkspace();

        Assert.Equal(PaneSide.Left, workspace.ActiveSide);
        Assert.Same(workspace.Left, workspace.ActivePane);
        Assert.Same(workspace.Right, workspace.InactivePane);
        Assert.Equal(GlobalViewState.Default.SplitterRatio, workspace.SplitterRatio);
        Assert.Null(workspace.WindowPlacement);
    }

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceViewModel(null!, CreatePane(), viewStates));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceViewModel(CreatePane(), null!, viewStates));
        Assert.Throws<ArgumentNullException>(
            () => new WorkspaceViewModel(CreatePane(), CreatePane(), null!));
    }

    // ── 활성 페인 ─────────────────────────────────────────────────

    [Fact]
    public void Activate_Right_SwapsTheActiveAndTheInactivePane()
    {
        var workspace = CreateWorkspace();

        workspace.ActivateCommand.Execute(PaneSide.Right);

        Assert.Equal(PaneSide.Right, workspace.ActiveSide);
        Assert.Same(workspace.Right, workspace.ActivePane);
        Assert.Same(workspace.Left, workspace.InactivePane);
    }

    [Fact]
    public void SwitchPane_Twice_ReturnsToTheOriginalPane()
    {
        var workspace = CreateWorkspace();

        workspace.SwitchPaneCommand.Execute(null);

        Assert.Equal(PaneSide.Right, workspace.ActiveSide);
        Assert.Same(workspace.Right, workspace.ActivePane);

        workspace.SwitchPaneCommand.Execute(null);

        Assert.Equal(PaneSide.Left, workspace.ActiveSide);
        Assert.Same(workspace.Left, workspace.ActivePane);
    }

    [Fact]
    public void Activate_RaisesPropertyChangedForBothDerivedPanes()
    {
        // View 가 활성 표시를 여기에 바인딩한다 (docs/DESIGN.md §6).
        var workspace = CreateWorkspace();
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.ActivateCommand.Execute(PaneSide.Right);

        Assert.Contains(nameof(WorkspaceViewModel.ActiveSide), changed);
        Assert.Contains(nameof(WorkspaceViewModel.ActivePane), changed);
        Assert.Contains(nameof(WorkspaceViewModel.InactivePane), changed);
    }

    [Fact]
    public void Activate_TheAlreadyActiveSide_StaysQuiet()
    {
        var workspace = CreateWorkspace();
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.ActivateCommand.Execute(PaneSide.Left);

        Assert.Empty(changed);
    }

    [Fact]
    public async Task Activate_KeepsBothSelections()
    {
        // 전환하면 선택이 사라지는 2분할은 쓸 수 없다 — 페인 간 복사의 출발점이 선택이다.
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300), ("b.txt", 100));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300), ("p2.jpg", 100));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(pics);
        workspace.Left.Selection.SelectSingle("a.txt");
        workspace.Right.Selection.SelectSingle("p2.jpg");

        workspace.ActivateCommand.Execute(PaneSide.Right);
        workspace.SwitchPaneCommand.Execute(null);

        Assert.Equal(["a.txt"], workspace.Left.Selection.SelectedNames);
        Assert.Equal("a.txt", workspace.Left.Selection.Anchor);
        Assert.Equal(["p2.jpg"], workspace.Right.Selection.SelectedNames);
        Assert.Equal("p2.jpg", workspace.Right.Selection.Anchor);
    }

    // ── 두 페인의 독립 ────────────────────────────────────────────

    [Fact]
    public async Task TheSameFolderOnBothSides_SortsIndependently()
    {
        // docs/PRD.md §4 — 같은 폴더를 양쪽 페인에 열 수 있고 각각 독립적인 뷰 상태를 갖는다.
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(folder);
        await workspace.Right.NavigateAsync(folder);

        await workspace.Left.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await workspace.Left.ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);

        Assert.Equal([new SortOrder(SortKey.Size)], workspace.Left.Sort);
        Assert.Equal(["b.txt", "c.txt", "a.txt"], workspace.Left.Items.Select(row => row.Name));

        Assert.Equal(FolderViewState.Default.Sort, workspace.Right.Sort);
        Assert.Equal(FolderViewState.Default.Mode, workspace.Right.ViewMode);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], workspace.Right.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task TheSameFolderOnBothSides_SelectsIndependently()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(folder);
        await workspace.Right.NavigateAsync(folder);

        workspace.Right.Selection.SelectSingle("c.txt");
        workspace.Left.Selection.SelectSingle("a.txt");
        workspace.Left.Selection.Toggle("b.txt");

        Assert.Equal(2, workspace.Left.Selection.Count);
        Assert.True(workspace.Left.Selection.IsSelected("a.txt"));
        Assert.True(workspace.Left.Selection.IsSelected("b.txt"));

        Assert.Equal(["c.txt"], workspace.Right.Selection.SelectedNames);
        Assert.Equal("c.txt", workspace.Right.Selection.Anchor);
    }

    [Fact]
    public async Task NavigatingOnePane_LeavesTheOtherWhereItWas()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(docs);

        await workspace.Left.NavigateAsync(pics);

        Assert.Equal(pics, workspace.Left.CurrentLocation);
        Assert.True(workspace.Left.CanGoBack);

        // 히스토리도 페인별이다 (docs/PRD.md §2).
        Assert.Equal(docs, workspace.Right.CurrentLocation);
        Assert.False(workspace.Right.CanGoBack);
        Assert.Equal(["a.txt"], workspace.Right.Items.Select(row => row.Name));
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

        await workspace.RestoreAsync();

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

        await workspace.RestoreAsync();

        Assert.Equal(0.15, workspace.SplitterRatio);
    }

    [Fact]
    public async Task RestoreAsync_WithAFailingStore_UsesTheDefault()
    {
        // 전역 뷰 상태는 캐시다 (CLAUDE.md §4). 읽지 못해도 창은 떠야 한다.
        var workspace = new WorkspaceViewModel(CreatePane(), CreatePane(), new FailingViewStateStore());

        await workspace.RestoreAsync();

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
        Assert.Equal(new GlobalViewState(0.4, workspace.WindowPlacement), saved);
    }

    [Fact]
    public async Task PersistAsync_WithAFailingStore_DoesNotThrow()
    {
        // 저장 실패가 창을 닫는 길을 막으면 안 된다.
        var workspace = new WorkspaceViewModel(CreatePane(), CreatePane(), new FailingViewStateStore());
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
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(pics);

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(pics, workspace.Left.CurrentLocation);
        Assert.Equal(["p1.jpg"], workspace.Left.Items.Select(row => row.Name));

        // 히스토리에 남는다 — 뒤로가 원래 폴더로 돌아간다.
        Assert.True(workspace.Left.CanGoBack);

        // 반대편은 움직이지 않는다.
        Assert.Equal(pics, workspace.Right.CurrentLocation);
    }

    [Fact]
    public async Task OpenOtherPaneLocation_FollowsTheActiveSide()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(pics);
        workspace.ActivateCommand.Execute(PaneSide.Right);

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(docs, workspace.Right.CurrentLocation);
        Assert.Equal(docs, workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task OpenOtherPaneLocation_WithNothingOpenOnTheOtherSide_DoesNothing()
    {
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300));
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(docs, workspace.Left.CurrentLocation);
        Assert.Equal(PaneStatus.Idle, workspace.Left.Status);
        Assert.Null(workspace.Right.CurrentLocation);

        // 열거를 다시 하지도 않는다.
        Assert.Single(source.EnumerateCalls);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    // ── 클릭에 의한 활성 전환 ─────────────────────────────────────

    [Fact]
    public async Task ItemClickInTheInactivePane_MakesThatPaneActive()
    {
        // 비활성 페인의 항목 클릭은 선택과 활성 전환이 한 동작이다 (목업 동작 그대로).
        // View 가 페인 전환을 따로 쏘면 클릭 한 번에 바인딩 두 개가 경합한다.
        var folder = Folder(@"C:\Temp", ("a.txt", 100));
        var workspace = CreateWorkspace();
        await workspace.Right.NavigateAsync(folder);

        workspace.Right.SelectItemCommand.Execute(workspace.Right.Items[0]);

        Assert.Equal(PaneSide.Right, workspace.ActiveSide);
        Assert.Equal(["a.txt"], workspace.Right.Selection.SelectedNames);
    }

    private WorkspaceViewModel CreateWorkspace() => new(CreatePane(), CreatePane(), viewStates);

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

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
