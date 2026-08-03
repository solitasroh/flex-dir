using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Formatting;
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
/// 뷰 모드와 정렬, 그리고 폴더별 기억 (docs/PRD.md §2).
/// <para>
/// 이름 순서와 크기 순서가 서로 다른 항목을 쓴다 — 정렬 기준이 실제로 바뀌었는지
/// <see cref="PaneViewModel.Sort"/> 값만 보고는 알 수 없고 목록 순서로 확인해야 한다.
/// </para>
/// <para>
/// 뷰 상태는 캐시다 (CLAUDE.md §4). 읽거나 쓰지 못해도 목록은 멀쩡해야 하고, 전환은
/// 이미 받은 항목을 다시 배치할 뿐 열거를 다시 시작하지 않는다.
/// </para>
/// </summary>
public class PaneViewStateTests
{
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 기본값 ────────────────────────────────────────────────────

    [Fact]
    public void New_StartsAtTheDefaultViewState()
    {
        var pane = CreatePane();

        Assert.Equal(FolderViewState.Default.Mode, pane.ViewMode);
        Assert.Equal(FolderViewState.Default.Sort, pane.Sort);
    }

    // ── 정렬 키 전환 ──────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_ToAnotherKey_StartsAscending()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Equal([new SortOrder(SortKey.Size)], pane.Sort);
        Assert.Equal(["b.txt", "c.txt", "a.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task ChangeSort_WithTheSameKey_FlipsTheDirection()
    {
        // 탐색기와 같은 동작이다 — 같은 헤더를 다시 누르면 방향만 뒤집힌다.
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Equal([new SortOrder(SortKey.Size, Descending: true)], pane.Sort);
        Assert.Equal(["a.txt", "c.txt", "b.txt"], pane.Items.Select(row => row.Name));

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Equal([new SortOrder(SortKey.Size)], pane.Sort);
        Assert.Equal(["b.txt", "c.txt", "a.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task ChangeSort_AfterADescendingKey_StartsTheNewKeyAscending()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Name);

        // 방향은 키를 따라가지 않는다.
        Assert.Equal([new SortOrder(SortKey.Name)], pane.Sort);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task ChangeSort_KeepsASingleKey()
    {
        // v1 UI 에는 다중 키를 만드는 조작이 없다. 없는 조작을 위한 상태를 쌓지 않는다.
        var folder = Folder(@"C:\Temp", ("a.txt", 300));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Modified);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Type);

        Assert.Equal([new SortOrder(SortKey.Type)], pane.Sort);
    }

    // ── 저장 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_SavesTheFolderViewState()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Modified);

        var saved = await viewStates.TryLoadAsync(folder, CancellationToken.None);
        Assert.Equal(new FolderViewState(ViewMode.Details, [new SortOrder(SortKey.Modified)]), saved);
    }

    [Fact]
    public async Task ChangeViewMode_SavesTheFolderViewStateWithoutRereadingTheFolder()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);

        Assert.Equal(ViewMode.Tiles, pane.ViewMode);

        var saved = await viewStates.TryLoadAsync(folder, CancellationToken.None);
        Assert.Equal(new FolderViewState(ViewMode.Tiles, FolderViewState.Default.Sort), saved);

        // 같은 데이터의 다른 표현일 뿐이다 (ADR-002 — DataTemplate 교체).
        Assert.Single(source.EnumerateCalls);
    }

    // ── 복원 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Navigate_RestoresTheRememberedViewState()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        await viewStates.SaveAsync(
            folder,
            new FolderViewState(ViewMode.Tiles, [new SortOrder(SortKey.Size, Descending: true)]),
            CancellationToken.None);
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(ViewMode.Tiles, pane.ViewMode);
        Assert.Equal([new SortOrder(SortKey.Size, Descending: true)], pane.Sort);

        // 열거를 시작하기 전에 읽었으므로 처음부터 그 정렬로 붙는다.
        Assert.Equal(["a.txt", "c.txt", "b.txt"], pane.Items.Select(row => row.Name));
        Assert.Single(source.EnumerateCalls);
    }

    [Fact]
    public async Task Navigate_WithNothingRemembered_UsesTheDefault()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100));
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(FolderViewState.Default.Mode, pane.ViewMode);
        Assert.Equal(FolderViewState.Default.Sort, pane.Sort);
        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Navigate_DoesNotCarryTheViewStateOverToTheNextFolder()
    {
        // 폴더별 기억이 이 저장소의 존재 이유다 (docs/PRD.md §2).
        var docs = Folder(@"C:\Temp\Docs", ("a.txt", 300), ("b.txt", 100));
        var pics = Folder(@"C:\Temp\Pics", ("p1.jpg", 300), ("p2.jpg", 100));
        var pane = CreatePane();
        await pane.NavigateAsync(docs);
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.LargeIcons);
        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        await pane.NavigateAsync(pics);

        Assert.Equal(FolderViewState.Default.Mode, pane.ViewMode);
        Assert.Equal(FolderViewState.Default.Sort, pane.Sort);
        Assert.Equal(["p1.jpg", "p2.jpg"], pane.Items.Select(row => row.Name));

        // 그리고 돌아오면 그 폴더의 설정이 그대로 있다.
        await pane.GoBackAsync();

        Assert.Equal(ViewMode.LargeIcons, pane.ViewMode);
        Assert.Equal([new SortOrder(SortKey.Size)], pane.Sort);
        Assert.Equal(["b.txt", "a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 선택 유지 ─────────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_KeepsTheSelection()
    {
        // docs/UI_GUIDE.md §원칙 4 — 선택은 이름으로 보관하므로 재배치로 잃지 않는다.
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.Selection.Toggle("c.txt");
        var summary = pane.StatusText;

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Equal(2, pane.Selection.Count);
        Assert.True(pane.Selection.IsSelected("a.txt"));
        Assert.True(pane.Selection.IsSelected("c.txt"));
        Assert.Equal("c.txt", pane.Selection.Anchor);
        Assert.Equal(summary, pane.StatusText);
    }

    [Fact]
    public async Task ChangeViewMode_KeepsTheSelection()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");
        var summary = pane.StatusText;

        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.LargeIcons);

        Assert.Equal(["b.txt"], pane.Selection.SelectedNames);
        Assert.Equal("b.txt", pane.Selection.Anchor);
        Assert.Equal(summary, pane.StatusText);
    }

    // ── 열지 않은 페인 ────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_BeforeAnyNavigation_DoesNotThrow()
    {
        // 저장할 폴더가 없다. 그렇다고 헤더 클릭이 예외가 되면 안 된다.
        var pane = CreatePane();

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.List);

        Assert.Equal([new SortOrder(SortKey.Size)], pane.Sort);
        Assert.Equal(ViewMode.List, pane.ViewMode);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(string.Empty, pane.StatusText);
    }

    // ── 저장소 실패 ───────────────────────────────────────────────

    [Fact]
    public async Task ViewStateFailures_FallBackToTheDefaultWithoutBlockingTheList()
    {
        // 뷰 설정을 못 읽는 것과 폴더를 못 여는 것은 사용자에게 전혀 다른 사건이다
        // (CLAUDE.md §4 — 뷰 상태는 캐시다).
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100));
        var pane = new PaneViewModel(
            source,
            typeNames,
            new FailingViewStateStore(),
            operations,
            clipboard,
            activator,
            dispatcher,
            Culture,
            TimeZoneInfo.Utc);

        await pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
        Assert.Equal(FolderViewState.Default.Mode, pane.ViewMode);
        Assert.Equal(FolderViewState.Default.Sort, pane.Sort);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
        Assert.Equal([new SortOrder(SortKey.Size)], pane.Sort);
        Assert.Equal(["b.txt", "a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 열거 중 전환 ──────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_WhileEnumerating_DoesNotRestartTheEnumeration()
    {
        // 10만 항목 폴더에서 헤더 클릭 한 번에 몇 초를 다시 기다리게 할 수 없다
        // (docs/UI_GUIDE.md §원칙 3).
        var folder = Folder(@"C:\Temp", ("a.txt", 300), ("b.txt", 100), ("c.txt", 200));
        source.YieldDelayMilliseconds = 50;
        var pane = CreatePane();

        var pending = pane.NavigateAsync(folder);
        Assert.Equal(PaneStatus.Enumerating, pane.Status);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);

        Assert.Single(source.EnumerateCalls);

        await pending;

        Assert.Single(source.EnumerateCalls);
        Assert.Equal(PaneStatus.Idle, pane.Status);

        // 이후 도착한 배치도 새 정렬로 자리를 잡는다.
        Assert.Equal(["b.txt", "c.txt", "a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 알림 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeSort_And_ChangeViewMode_RaisePropertyChanged()
    {
        var folder = Folder(@"C:\Temp", ("a.txt", 300));
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        var changed = new List<string?>();
        pane.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await pane.ChangeSortCommand.ExecuteAsync(SortKey.Size);
        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.Tiles);

        Assert.Contains(nameof(PaneViewModel.Sort), changed);
        Assert.Contains(nameof(PaneViewModel.ViewMode), changed);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneViewModel CreatePane()
        => new(source, typeNames, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    /// <summary>크기를 함께 준다 — 이름 순서와 크기 순서가 달라야 정렬 전환이 보인다.</summary>
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

    /// <summary>읽기도 쓰기도 실패하는 저장소. 설정 파일이 손상됐거나 잠긴 상황이다.</summary>
    private sealed class FailingViewStateStore : IViewStateStore
    {
        public ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct)
            => throw new IOException("뷰 상태를 읽을 수 없다.");

        public ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct)
            => throw new IOException("뷰 상태를 쓸 수 없다.");

        public ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
            => throw new IOException("뷰 상태를 읽을 수 없다.");

        public ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct)
            => throw new IOException("뷰 상태를 쓸 수 없다.");
    }
}
