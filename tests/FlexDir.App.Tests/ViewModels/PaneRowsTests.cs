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
/// wrap 뷰 3종의 합성 행 (ADR-016 · docs/ARCHITECTURE.md §5).
/// <para>
/// 열 수는 ViewModel 이 정한다 — View 는 <see cref="PaneViewModel.SetViewportSize"/> 로
/// 뷰포트 크기만 민다. 치수 표(docs/DESIGN.md §2)에서: 목록은 행 높이 22 로 세로를,
/// 타일은 220+8=228 로, 큰 아이콘은 116+8=124 로 가로를 나눈다. Details 는 합성 행을
/// 지나지 않는다 — 평평한 <c>Items</c> 가 주 경로다.
/// </para>
/// </summary>
public class PaneRowsTests
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
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── Details 는 합성 행을 지나지 않는다 ────────────────────────

    [Fact]
    public void New_HasNoRows()
    {
        var pane = CreatePane();

        Assert.Empty(pane.Rows);
    }

    [Fact]
    public async Task Details_DoesNotBuildRows()
    {
        // 10만 항목의 주 경로에 래퍼를 두지 않는다 (ADR-016). 소스가 둘인 것이 의도다.
        var pane = await OpenAsync(8);

        pane.SetViewportSize(1000, 600);

        Assert.Equal(ViewMode.Details, pane.ViewMode);
        Assert.Empty(pane.Rows);
    }

    // ── 열 수 계산 (docs/DESIGN.md §2 치수 표) ────────────────────

    [Theory]
    [InlineData(684, 3)]  // 228 × 3 — 경계에서 세 번째 열이 생긴다
    [InlineData(683, 2)]  // 한 픽셀 모자라면 두 열이다
    [InlineData(228, 1)]
    [InlineData(10, 1)]   // 아무리 좁아도 한 줄에 하나는 놓는다
    public async Task Tiles_ColumnCountComesFromTheViewportWidth(double width, int expected)
    {
        var pane = await OpenAsync(8, ViewMode.Tiles);

        pane.SetViewportSize(width, 400);

        Assert.Equal(expected, pane.Rows[0].Items.Count);
    }

    [Theory]
    [InlineData(496, 4)]  // 124 × 4
    [InlineData(495, 3)]
    public async Task LargeIcons_ColumnCountComesFromTheViewportWidth(double width, int expected)
    {
        var pane = await OpenAsync(8, ViewMode.LargeIcons);

        pane.SetViewportSize(width, 400);

        Assert.Equal(expected, pane.Rows[0].Items.Count);
    }

    [Theory]
    [InlineData(66, 3)]   // 22 × 3 — 목록 뷰만 높이로 정한다 (세로로 채운다)
    [InlineData(65, 2)]
    [InlineData(0, 1)]    // 최소 1개 보장
    public async Task List_ColumnCountComesFromTheViewportHeight(double height, int expected)
    {
        var pane = await OpenAsync(8, ViewMode.List);

        pane.SetViewportSize(400, height);

        Assert.Equal(expected, pane.Rows[0].Items.Count);
    }

    // ── 묶음 내용 ─────────────────────────────────────────────────

    [Fact]
    public async Task Rows_ChunkTheItemsInDisplayOrderWithoutCopies()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles);

        pane.SetViewportSize(700, 400);  // 3열

        Assert.Equal([3, 3, 2], pane.Rows.Select(row => row.Items.Count));
        Assert.Equal(
            pane.Items.Select(row => row.Name),
            pane.Rows.SelectMany(row => row.Items).Select(row => row.Name));

        // 같은 인스턴스를 공유한다 — 썸네일·아이콘 알림이 행 너머로 이어져야 한다.
        Assert.Same(pane.Items[0], pane.Rows[0].Items[0]);
    }

    [Fact]
    public async Task Rows_CarryTheSlotCountSoThePartialRowMatchesTheFullRows()
    {
        // 타일 칸은 가변 폭이다 (docs/DESIGN.md §2 — "최소 폭 220, 가변"). 마지막 줄이 두
        // 칸뿐이어도 칸 너비는 윗줄과 같아야 하므로, 줄이 자기 칸 수를 들고 있어야 한다.
        var pane = await OpenAsync(8, ViewMode.Tiles);

        pane.SetViewportSize(700, 400);  // 3열

        Assert.Equal([3, 3, 3], pane.Rows.Select(row => row.Capacity));
        Assert.Equal([3, 3, 2], pane.Rows.Select(row => row.Items.Count));
    }

    // ── 열 수가 바뀔 때만 다시 만든다 ─────────────────────────────

    [Fact]
    public async Task Rows_KeepTheSameInstanceWhileTheColumnCountIsUnchanged()
    {
        // 스플리터 드래그 중에도 즉시 반영하되, 열 수가 그대로면 아무 일도 하지 않는다
        // (docs/DESIGN.md §9-1). 시간 디바운스를 쓰지 않는다.
        var pane = await OpenAsync(8, ViewMode.Tiles);
        pane.SetViewportSize(700, 400);
        var rows = pane.Rows;

        pane.SetViewportSize(750, 400);   // 여전히 3열
        Assert.Same(rows, pane.Rows);

        pane.SetViewportSize(912, 400);   // 4열 — 여기서만 다시 만든다
        Assert.NotSame(rows, pane.Rows);
        Assert.Equal([4, 4], pane.Rows.Select(row => row.Items.Count));
    }

    [Fact]
    public async Task Rows_RaisePropertyChangedOnlyWhenTheColumnCountChanges()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles);
        pane.SetViewportSize(700, 400);
        _ = pane.Rows;

        var changes = 0;
        pane.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PaneViewModel.Rows))
            {
                changes++;
            }
        };

        pane.SetViewportSize(750, 400);
        Assert.Equal(0, changes);

        pane.SetViewportSize(912, 400);
        Assert.Equal(1, changes);
    }

    // ── 뷰 전환 ───────────────────────────────────────────────────

    [Fact]
    public async Task SwitchingViewMode_RebuildsForTheNewDimensionTable()
    {
        var pane = await OpenAsync(8, ViewMode.Tiles);
        pane.SetViewportSize(700, 400);
        Assert.Equal(3, pane.Rows[0].Items.Count);

        // 같은 뷰포트라도 큰 아이콘은 폭 124 로 나눈다 — 700/124 = 5.
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.LargeIcons);
        Assert.Equal(5, pane.Rows[0].Items.Count);

        // Details 로 돌아오면 합성 행은 비운다.
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.Details);
        Assert.Empty(pane.Rows);
    }

    // ── 목록이 바뀌면 행도 따라간다 ───────────────────────────────

    [Fact]
    public async Task Refresh_RebuildsTheRowsFromTheNewList()
    {
        var folder = Folder(@"C:\Temp", 8);
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);
        pane.SetViewportSize(700, 400);
        Assert.Equal([3, 3, 2], pane.Rows.Select(row => row.Items.Count));

        Folder(@"C:\Temp", 4);
        await pane.RefreshAsync();

        Assert.Equal([3, 1], pane.Rows.Select(row => row.Items.Count));
        Assert.Equal(
            pane.Items.Select(row => row.Name),
            pane.Rows.SelectMany(row => row.Items).Select(row => row.Name));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private async Task<PaneViewModel> OpenAsync(int itemCount, ViewMode? mode = null)
    {
        var folder = Folder(@"C:\Temp", itemCount);
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        if (mode is { } target)
        {
            await pane.ChangeViewModeCommand.ExecuteAsync(target);
        }

        return pane;
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>이름순 정렬이 자명하도록 a00…a07 로 만든다.</summary>
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
