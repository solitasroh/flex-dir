using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Grouping;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Sorting;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

// System.Globalization 에도 SortKey 가 있다. 여기서 쓰는 것은 정렬 기준 쪽이다.
using SortKey = FlexDir.Core.Sorting.SortKey;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// Details 그룹화 (docs/PRD-v2.md §6-1).
/// <para>
/// 전작은 그룹핑으로 데이터 가상화를 깨뜨렸다 (docs/PRD.md §3). 여기서는 ADR-016 의 합성
/// 행과 같은 수를 쓴다 — 헤더도 행이면 <c>VirtualizingStackPanel</c> 이 그대로 산다.
/// 그룹 경계는 <b>정렬된 목록의 인접 비교</b>로만 잡으므로, 그룹 키가 정렬 1차 키가 되는
/// 것이 이 기능의 뼈대다 (<see cref="FileItemGroups.WithGroupKey"/>).
/// </para>
/// </summary>
public class PaneGroupingTests
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

    // ── 꺼진 것이 기본이다 ────────────────────────────────────────
    // 그룹화가 꺼져 있으면 목록 경로가 v1 과 한 글자도 다르지 않아야 한다 —
    // 10만 항목의 주 경로에 래퍼를 두지 않는다 (ADR-016).

    [Fact]
    public async Task Default_IsNotGrouped()
    {
        var pane = await OpenAsync();

        Assert.Null(pane.GroupBy);
        Assert.False(pane.IsGrouped);
        Assert.Empty(pane.DetailRows);
    }

    [Fact]
    public async Task TurningGroupingOff_EmptiesTheProjection()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ChangeGroupCommand.ExecuteAsync(null);

        Assert.Null(pane.GroupBy);
        Assert.Empty(pane.DetailRows);
    }

    // 그룹화는 Details 전용이다. wrap 뷰 3종은 합성 행(Rows)을 지난다.
    [Fact]
    public async Task WrapViews_DoNotBuildDetailRows()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);

        Assert.Empty(pane.DetailRows);
    }

    // ── 분류 방법 메뉴의 항목 (툴바 · 헤더 우클릭이 함께 쓴다) ────
    // 목록을 ViewModel 이 소유한다. XAML 두 곳에 항목을 두 벌 쓰면 곧 어긋나고,
    // 체크 표시가 맞는지도 자동 채점할 수 없게 된다.

    [Fact]
    public async Task GroupOptions_AreTheFourKeysPlusNone()
    {
        var pane = await OpenAsync();

        Assert.Equal(
            ["없음", "이름", "크기", "유형", "수정한 날짜"],
            pane.GroupOptions.Select(option => option.Label));

        Assert.Equal(
            [null, SortKey.Name, SortKey.Size, SortKey.Type, SortKey.Modified],
            pane.GroupOptions.Select(option => option.Key));
    }

    [Fact]
    public async Task GroupOptions_MarkNoneWhenGroupingIsOff()
    {
        var pane = await OpenAsync();

        Assert.Equal("없음", Assert.Single(pane.GroupOptions, option => option.IsSelected).Label);
    }

    [Fact]
    public async Task GroupOptions_FollowTheCurrentGroupKey()
    {
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        Assert.Equal("유형", Assert.Single(pane.GroupOptions, option => option.IsSelected).Label);
    }

    // 목록 인스턴스가 그대로면 View 가 체크 표시를 다시 그릴 신호를 못 받는다.
    [Fact]
    public async Task GroupOptions_AreRebuiltWhenTheGroupKeyChanges()
    {
        var pane = await OpenAsync();
        var before = pane.GroupOptions;

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Size);

        Assert.NotSame(before, pane.GroupOptions);
    }

    // ── 헤더 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Grouping_PutsOneHeaderBeforeEachGroup()
    {
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        Assert.Equal(
            ["폴더", "MD", "PNG", "TXT"],
            pane.DetailRows.Where(row => row.IsHeader).Select(row => row.Label));
    }

    [Fact]
    public async Task Header_CarriesTheItemCountOfItsGroup()
    {
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        var headers = pane.DetailRows.Where(row => row.IsHeader).ToArray();

        Assert.Equal([1, 1, 2, 3], headers.Select(row => row.Count));
        Assert.Equal(pane.Items.Count, headers.Sum(row => row.Count));
    }

    [Fact]
    public async Task ItemRows_KeepTheOrderOfItems()
    {
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        Assert.Equal(
            pane.Items.Select(item => item.Name),
            pane.DetailRows.Where(row => !row.IsHeader).Select(row => row.Item!.Name));
    }

    // 그룹 키가 정렬 1차 키가 되지 않으면 같은 라벨의 헤더가 두 자리에 생긴다.
    [Fact]
    public async Task GroupKey_BecomesThePrimarySortKey()
    {
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        var labels = pane.DetailRows.Where(row => row.IsHeader).Select(row => row.Label).ToArray();

        Assert.Equal(labels.Distinct().Count(), labels.Length);
    }

    // ── 접기 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Collapsing_HidesTheItemsButKeepsTheHeader()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ToggleGroupCommand.ExecuteAsync("TXT");

        var header = pane.DetailRows.Single(row => row.IsHeader && row.Label == "TXT");

        Assert.True(header.IsCollapsed);
        Assert.Equal(3, header.Count);        // 개수는 접혀도 그대로 보인다
        Assert.DoesNotContain(pane.DetailRows, row => !row.IsHeader && row.Item!.Name.EndsWith(".txt"));
    }

    [Fact]
    public async Task Collapsing_LeavesOtherGroupsAlone()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ToggleGroupCommand.ExecuteAsync("TXT");

        Assert.Contains(pane.DetailRows, row => !row.IsHeader && row.Item!.Name.EndsWith(".png"));
    }

    [Fact]
    public async Task TogglingTwice_ExpandsAgain()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ToggleGroupCommand.ExecuteAsync("TXT");
        await pane.ToggleGroupCommand.ExecuteAsync("TXT");

        Assert.Equal(pane.Items.Count, pane.DetailRows.Count(row => !row.IsHeader));
    }

    // 항목이 화면에 없으면 키보드도 그것을 건너뛴다. 그러지 않으면 접힌 그룹 안에서
    // 포커스가 사라지고 스크롤이 엉뚱한 자리로 간다.
    [Fact]
    public async Task CollapsedGroups_AreSkippedByKeyboardNavigation()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);
        await pane.ToggleGroupCommand.ExecuteAsync("MD");

        pane.MoveFocus(FocusMove.Home, extend: false, toggleOnly: false);
        var visited = new List<string> { pane.FocusedName! };

        for (var step = 0; step < 5; step++)
        {
            pane.MoveFocus(FocusMove.Down, extend: false, toggleOnly: false);
            visited.Add(pane.FocusedName!);
        }

        Assert.DoesNotContain("note.md", visited);
    }

    [Fact]
    public async Task CollapsedGroups_AreSkippedByEnd()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);
        await pane.ToggleGroupCommand.ExecuteAsync("TXT");

        pane.MoveFocus(FocusMove.End, extend: false, toggleOnly: false);

        Assert.DoesNotContain(".txt", pane.FocusedName!, StringComparison.Ordinal);
    }

    // ── 폴더별 기억 (docs/PRD.md §2) ──────────────────────────────

    [Fact]
    public async Task Grouping_IsRemembered()
    {
        var folder = Folder();
        var pane = await OpenAsync();

        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Size);

        var saved = await viewStates.TryLoadAsync(folder, CancellationToken.None);

        Assert.Equal(SortKey.Size, saved!.GroupBy);
    }

    [Fact]
    public async Task CollapsedGroups_AreRemembered()
    {
        var folder = Folder();
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        await pane.ToggleGroupCommand.ExecuteAsync("TXT");

        var saved = await viewStates.TryLoadAsync(folder, CancellationToken.None);

        Assert.Equal(["TXT"], saved!.Collapsed);
    }

    [Fact]
    public async Task RememberedGrouping_IsRestoredWhenTheFolderIsOpened()
    {
        var folder = Folder();
        await viewStates.SaveAsync(
            folder,
            new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Name)], SortKey.Type, ["TXT"]),
            CancellationToken.None);

        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        Assert.Equal(SortKey.Type, pane.GroupBy);
        Assert.True(pane.DetailRows.Single(row => row.IsHeader && row.Label == "TXT").IsCollapsed);
    }

    // ── 갱신 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RebuildsTheHeaders()
    {
        var pane = await OpenAsync();
        await pane.ChangeGroupCommand.ExecuteAsync(SortKey.Type);

        Folder(withExtraPng: true);
        await pane.RefreshAsync();

        Assert.Equal(3, pane.DetailRows.Single(row => row.IsHeader && row.Label == "PNG").Count);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private async Task<PaneViewModel> OpenAsync()
    {
        var folder = Folder();
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        return pane;
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>
    /// 폴더 1 · MD 1 · PNG 2 · TXT 3. 확장자별 개수가 서로 달라 헤더의 개수를 눈으로
    /// 대조할 수 있다.
    /// </summary>
    private LocationId Folder(bool withExtraPng = false)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out var error), $"파싱 실패: {error}");

        var names = new List<(string Name, FileItemFlags Flags)>
        {
            ("sub", FileItemFlags.Directory),
            ("note.md", FileItemFlags.None),
            ("a.png", FileItemFlags.None),
            ("b.png", FileItemFlags.None),
            ("a.txt", FileItemFlags.None),
            ("b.txt", FileItemFlags.None),
            ("c.txt", FileItemFlags.None),
        };

        if (withExtraPng)
        {
            names.Add(("c.png", FileItemFlags.None));
        }

        source.Folders[folder] =
        [
            .. names.Select(entry => new FileItem(
                entry.Name,
                folder.Combine(entry.Name),
                1024,
                DateTimeOffset.UnixEpoch,
                entry.Flags)),
        ];

        return folder;
    }
}
