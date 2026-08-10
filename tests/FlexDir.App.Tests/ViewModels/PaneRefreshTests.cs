using System.Collections.Specialized;
using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 같은 폴더를 다시 읽을 때 목록이 어떻게 갱신되는가 (docs/PRD-v2.md §13).
///
/// <para>
/// <b>왜 이것이 문제인가</b>: <c>Reset</c> 알림은 WPF 목록의 컨테이너를 통째로 다시
/// 만들어 스크롤과 선택 표시를 날린다 — ADR-011 이 감시 갱신 경로에 대해 배운 것이다.
/// 그런데 <b>열거 경로는 여전히 <c>ReplaceAll</c></b> 이었다. 로컬에서는 F5 를 누를 때만
/// 지나서 보이지 않았고, WSL 처럼 감시가 되풀이 실패하는 경로에서는 백오프가 걸린 뒤에도
/// 30초마다 지나 목록이 계속 깜박였다 (사용자 신고 2026-08-10).
/// </para>
///
/// <para>
/// <b>폴더를 옮길 때는 그대로 교체한다.</b> 거기서는 이전 폴더의 내용을 지우는 것이
/// 목적이고, 병합하면 두 폴더의 항목이 잠깐 섞인다.
/// </para>
/// </summary>
public class PaneRefreshTests
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

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);

        source.Folders[folder] =
        [
            .. names.Select(name => new FileItem(
                name, folder.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.None)),
        ];

        return folder;
    }

    /// <summary>목록에 간 알림을 모은다. Reset 이 섞였는지가 이 테스트의 관심사다.</summary>
    private static List<NotifyCollectionChangedAction> Watch(PaneViewModel pane)
    {
        var seen = new List<NotifyCollectionChangedAction>();

        pane.Items.CollectionChanged += (_, args) => seen.Add(args.Action);

        return seen;
    }

    [Fact]
    public async Task RefreshingTheSameFolder_SendsNoResetNotification()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        var seen = Watch(pane);

        await pane.RefreshAsync();

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, seen);
    }

    [Fact]
    public async Task RefreshingAnUnchangedFolder_TouchesNothingAtAll()
    {
        // 아무것도 바뀌지 않았으면 알림도 없어야 한다 — 그래야 스크롤도 선택도 그대로다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        var seen = Watch(pane);

        await pane.RefreshAsync();

        Assert.Empty(seen);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task RefreshingKeepsTheSelection()
    {
        // 사용자가 신고한 증상이다 — WSL 에서 30초마다 선택이 풀렸다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");

        await pane.RefreshAsync();

        Assert.Equal(["b.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task RefreshingPicksUpWhatChanged()
    {
        // 깜박임을 없애자고 갱신을 놓치면 안 된다 — 새로고침을 부른 이유가 그것이다.
        var folder = Folder(@"C:\Temp", "a.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        source.Folders[folder].Add(
            new FileItem("b.txt", folder.Combine("b.txt"), 0, DateTimeOffset.UnixEpoch, FileItemFlags.None));
        source.Folders[folder].RemoveAll(item => item.Name == "c.txt");

        await pane.RefreshAsync();

        // 정렬 자리도 지킨다 — 맨 끝에 붙이지 않는다.
        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task RefreshingReusesRowsThatDidNotChange()
    {
        // 인스턴스가 그대로여야 그 줄의 아이콘을 다시 묻지 않는다 (ThumbnailAttempted).
        // 새로 만들면 30초마다 그 폴더의 아이콘을 전부 다시 조회하게 되고, 9P 경로에서는
        // 항목당 250ms 다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        var before = pane.Items.ToList();

        await pane.RefreshAsync();

        Assert.Same(before[0], pane.Items[0]);
        Assert.Same(before[1], pane.Items[1]);
    }

    [Fact]
    public async Task MovingToAnotherFolder_StillReplacesTheList()
    {
        // 폴더 전환에서는 교체가 맞다 — 병합하면 두 폴더의 항목이 잠깐 섞인다.
        var docs = Folder(@"C:\Docs", "a.txt");
        var pics = Folder(@"C:\Pics", "z.png");
        await using var pane = CreatePane();
        await pane.NavigateAsync(docs);
        var seen = Watch(pane);

        await pane.NavigateAsync(pics);

        Assert.Contains(NotifyCollectionChangedAction.Reset, seen);
        Assert.Equal(["z.png"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task RefreshingAFolderThatBecameEmpty_EmptiesTheList()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        source.Folders[folder].Clear();

        await pane.RefreshAsync();

        Assert.Empty(pane.Items);
        Assert.Equal(PaneStatus.Empty, pane.Status);
    }

    [Fact]
    public async Task RefreshingManyItems_StillArrivesInSortedOrder()
    {
        // 열거는 배치로 온다 (256개). 병합하려면 전부 모아 다시 정렬해야 하는데, 배치
        // 안에서만 정렬하면 배치 경계에서 순서가 어긋난다.
        var names = Enumerable.Range(1, 600).Select(n => $"f{n:D4}.txt").ToArray();
        var folder = Folder(@"C:\Temp", names);
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.RefreshAsync();

        Assert.Equal(names.Order(StringComparer.Ordinal), pane.Items.Select(row => row.Name));
    }
}
