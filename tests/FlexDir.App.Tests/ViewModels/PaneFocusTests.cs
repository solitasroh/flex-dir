using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 키보드 포커스와 2D 이동 (docs/DESIGN.md §9 · ADR-016).
/// <para>
/// 합성 행 위에서 <c>ListView</c> 내장 이동은 행 단위로만 움직이므로 이동·선택은 전부
/// ViewModel 이 한다. 줄을 건너는 축은 뷰가 정한다 — 타일·큰 아이콘은 가로로 채워
/// <c>↓</c> 가 한 줄 수만큼 뒤이고, 목록 뷰만 세로로 채워 <c>↓</c> 가 같은 열 아래·
/// <c>→</c> 가 다음 열이다.
/// </para>
/// </summary>
public class PaneFocusTests
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

    // ── 시작 상태 ─────────────────────────────────────────────────

    [Fact]
    public void New_HasNoFocus()
    {
        var pane = CreatePane();

        Assert.Null(pane.FocusedName);
    }

    [Fact]
    public async Task MoveFocus_WithNoFocus_StartsAtTheFirstItem()
    {
        var pane = await OpenAsync(8);

        Move(pane, FocusMove.Down);

        Assert.Equal("a00.txt", pane.FocusedName);
        Assert.Equal(["a00.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task MoveFocus_End_WithNoFocus_StartsAtTheLastItem()
    {
        var pane = await OpenAsync(8);

        Move(pane, FocusMove.End);

        Assert.Equal("a07.txt", pane.FocusedName);
    }

    [Fact]
    public void MoveFocus_OnAnEmptyList_DoesNothing()
    {
        var pane = CreatePane();

        pane.MoveFocus(FocusMove.Down, extend: false, toggleOnly: false);

        Assert.Null(pane.FocusedName);
        Assert.Equal(0, pane.Selection.Count);
    }

    // ── Details — 열이 하나다 ─────────────────────────────────────

    [Fact]
    public async Task Details_MovesOneItemPerVerticalStep()
    {
        var pane = await OpenAsync(8);

        Move(pane, FocusMove.Down);      // a00
        Move(pane, FocusMove.Down);
        Move(pane, FocusMove.Down);
        Assert.Equal("a02.txt", pane.FocusedName);

        Move(pane, FocusMove.Up);
        Assert.Equal("a01.txt", pane.FocusedName);
    }

    [Fact]
    public async Task Details_IgnoresHorizontalMoves()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.Right);
        Move(pane, FocusMove.Left);

        Assert.Equal("a00.txt", pane.FocusedName);
    }

    // ── 타일 — 가로로 채우므로 ↓ 는 한 줄 수만큼 뒤 ──────────────

    [Fact]
    public async Task Tiles_DownMovesOneRowOfThreeAhead()
    {
        // 뷰포트 700 → 700/228 = 3열. 8개면 [0..2] [3..5] [6..7] 세 줄이다.
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.Down);
        Assert.Equal("a03.txt", pane.FocusedName);

        Move(pane, FocusMove.Down);
        Assert.Equal("a06.txt", pane.FocusedName);
    }

    [Fact]
    public async Task Tiles_DownOnTheLastRowStaysPut()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.End);       // a07 — 마지막 줄

        Move(pane, FocusMove.Down);

        Assert.Equal("a07.txt", pane.FocusedName);
    }

    [Fact]
    public async Task Tiles_DownIntoAShorterLastRowStopsAtTheLastItem()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.Down);      // a00
        Move(pane, FocusMove.Down);      // a03
        Move(pane, FocusMove.Right);     // a04
        Move(pane, FocusMove.Right);     // a05

        // 아래 칸(8번)이 없다 — 마지막 항목까지만 간다.
        Move(pane, FocusMove.Down);

        Assert.Equal("a07.txt", pane.FocusedName);
    }

    [Fact]
    public async Task Tiles_UpOnTheFirstRowStaysPut()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.Down);      // a00
        Move(pane, FocusMove.Right);     // a01

        Move(pane, FocusMove.Up);

        Assert.Equal("a01.txt", pane.FocusedName);
    }

    [Fact]
    public async Task Tiles_HorizontalMovesFollowDisplayOrderAcrossRows()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.Left);      // 처음에서는 제자리
        Assert.Equal("a00.txt", pane.FocusedName);

        Move(pane, FocusMove.Right);
        Move(pane, FocusMove.Right);
        Move(pane, FocusMove.Right);     // 줄 끝을 넘어 다음 줄 첫 항목으로 (탐색기와 같다)
        Assert.Equal("a03.txt", pane.FocusedName);
    }

    // ── 목록 — 세로로 채우므로 방향이 뒤집힌다 ────────────────────

    [Fact]
    public async Task List_DownMovesWithinTheColumn_RightMovesToTheNextColumn()
    {
        // 뷰포트 높이 66 → 66/22 = 열당 3개. [0..2] [3..5] [6..7] 세 열이다.
        var pane = await OpenAsync(8, ViewMode.List, 400, 66);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.Down);      // 같은 열 아래
        Assert.Equal("a01.txt", pane.FocusedName);

        Move(pane, FocusMove.Right);     // 다음 열
        Assert.Equal("a04.txt", pane.FocusedName);

        Move(pane, FocusMove.Left);      // 이전 열
        Assert.Equal("a01.txt", pane.FocusedName);

        Move(pane, FocusMove.Up);
        Assert.Equal("a00.txt", pane.FocusedName);
    }

    [Fact]
    public async Task List_RightOnTheLastColumnStaysPut()
    {
        var pane = await OpenAsync(8, ViewMode.List, 400, 66);
        Move(pane, FocusMove.End);       // a07 — 마지막 열

        Move(pane, FocusMove.Right);

        Assert.Equal("a07.txt", pane.FocusedName);
    }

    // ── Home · End · PageUp · PageDown ────────────────────────────

    [Fact]
    public async Task HomeAndEnd_JumpToTheEnds()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.End);
        Assert.Equal("a07.txt", pane.FocusedName);

        Move(pane, FocusMove.Home);
        Assert.Equal("a00.txt", pane.FocusedName);
    }

    [Theory]
    [InlineData(ViewMode.Details, 400, 240, "a10.txt")]    // 240/24 = 10줄 × 1
    [InlineData(ViewMode.List, 700, 66, "a09.txt")]        // 700/212 = 3열 × 열당 3
    [InlineData(ViewMode.Tiles, 700, 400, "a18.txt")]      // 400/60 = 6줄 × 3
    [InlineData(ViewMode.LargeIcons, 496, 300, "a08.txt")] // 300/148 = 2줄 × 4
    public async Task PageDown_MovesOneViewportOfLines(ViewMode mode, double width, double height, string expected)
    {
        // 줄 간격은 DESIGN.md §2 의 행 높이 + 세로 간격이다 (Details 24 · 목록 열 폭
        // 200+12 · 타일 56+4 · 큰 아이콘 140+8).
        var pane = await OpenAsync(25, mode, width, height);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.PageDown);
        Assert.Equal(expected, pane.FocusedName);

        Move(pane, FocusMove.PageUp);
        Assert.Equal("a00.txt", pane.FocusedName);
    }

    [Fact]
    public async Task PageDown_StopsAtTheLastItem()
    {
        var pane = await OpenAsync(8, ViewMode.Details, 400, 240);
        Move(pane, FocusMove.Down);      // a00

        Move(pane, FocusMove.PageDown);  // 한 화면(10)보다 목록이 짧다

        Assert.Equal("a07.txt", pane.FocusedName);
    }

    // ── 선택과의 결합 (docs/DESIGN.md §9) ─────────────────────────

    [Fact]
    public async Task MoveFocus_PlainMove_SelectsTheTargetAlone()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);      // a00
        Move(pane, FocusMove.Down);      // a01

        Assert.Equal(["a01.txt"], pane.Selection.SelectedNames);
        Assert.Equal("a01.txt", pane.Selection.Anchor);
    }

    [Fact]
    public async Task MoveFocus_WithExtend_GrowsTheRangeFromTheAnchor()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);      // a00 — 앵커

        pane.MoveFocus(FocusMove.Down, extend: true, toggleOnly: false);
        pane.MoveFocus(FocusMove.Down, extend: true, toggleOnly: false);

        Assert.Equal("a02.txt", pane.FocusedName);
        Assert.Equal(3, pane.Selection.Count);
        Assert.True(pane.Selection.IsSelected("a00.txt"));
        Assert.True(pane.Selection.IsSelected("a01.txt"));
        Assert.True(pane.Selection.IsSelected("a02.txt"));

        // 앵커는 그대로다 — Shift 를 누른 채 방향을 바꿀 수 있어야 한다.
        Assert.Equal("a00.txt", pane.Selection.Anchor);
    }

    [Fact]
    public async Task MoveFocus_WithExtend_RangesOverTheDisplayOrderIn2D()
    {
        // 타일에서 Shift+↓ 는 한 줄 수만큼의 범위다 — 범위는 화면 순서로 잇는다.
        var pane = await OpenAsync(8, ViewMode.Tiles, 700, 400);
        Move(pane, FocusMove.Down);      // a00

        pane.MoveFocus(FocusMove.Down, extend: true, toggleOnly: false);

        Assert.Equal("a03.txt", pane.FocusedName);
        Assert.Equal(4, pane.Selection.Count);
    }

    [Fact]
    public async Task MoveFocus_WithToggleOnly_LeavesTheSelectionAlone()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);      // a00 선택

        pane.MoveFocus(FocusMove.Down, extend: false, toggleOnly: true);

        // 포커스만 옮겨 갔다 — Ctrl+Space 가 나중에 a01 을 토글할 수 있는 상태다.
        Assert.Equal("a01.txt", pane.FocusedName);
        Assert.Equal(["a00.txt"], pane.Selection.SelectedNames);
    }

    // ── 목록 변화와 포커스 ────────────────────────────────────────

    [Fact]
    public async Task FocusedName_IsClearedWhenTheFolderChanges()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);

        var other = Folder(@"C:\Other", 3);
        await pane.NavigateAsync(other);

        Assert.Null(pane.FocusedName);
    }

    [Fact]
    public async Task FocusedName_SurvivesARefreshOfTheSameFolder()
    {
        // 같은 폴더를 다시 읽는 것은 선택을 유지한다 (CLAUDE.md §4) — 포커스도 같다.
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.Down);

        await pane.RefreshAsync();

        Assert.Equal("a00.txt", pane.FocusedName);
    }

    [Fact]
    public async Task MoveFocus_WhenTheFocusedItemDisappeared_RestartsFromTheFirstItem()
    {
        var pane = await OpenAsync(8);
        Move(pane, FocusMove.End);       // a07

        Folder(@"C:\Temp", 4);           // a07 이 사라졌다
        await pane.RefreshAsync();

        Move(pane, FocusMove.Down);

        Assert.Equal("a00.txt", pane.FocusedName);
    }

    [Fact]
    public async Task FocusedName_RaisesPropertyChanged()
    {
        var pane = await OpenAsync(8);

        var changed = new List<string?>();
        pane.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Move(pane, FocusMove.Down);

        Assert.Contains(nameof(PaneViewModel.FocusedName), changed);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private static void Move(PaneViewModel pane, FocusMove move)
        => pane.MoveFocus(move, extend: false, toggleOnly: false);

    private async Task<PaneViewModel> OpenAsync(
        int itemCount,
        ViewMode mode = ViewMode.Details,
        double width = 400,
        double height = 600)
    {
        var folder = Folder(@"C:\Temp", itemCount);
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        if (mode != ViewMode.Details)
        {
            await pane.ChangeViewModeCommand.ExecuteAsync(mode);
        }

        pane.SetViewportSize(width, height);

        return pane;
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    /// <summary>이름순 정렬이 자명하도록 a00…aNN 으로 만든다.</summary>
    private LocationId Folder(string path, int itemCount)
    {
        Assert.True(LocationId.TryParse(path, out var folder, out var error), $"파싱 실패: {error}");

        source.Folders[folder] = [.. Enumerable.Range(0, itemCount).Select(index => new FileItem(
            $"a{index:00}.txt",
            folder.Combine($"a{index:00}.txt"),
            1024,
            DateTimeOffset.UnixEpoch,
            FileItemFlags.None))];

        return folder;
    }
}
