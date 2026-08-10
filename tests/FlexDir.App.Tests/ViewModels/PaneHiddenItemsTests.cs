using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 숨김·시스템 파일 보기 (docs/PRD-v2.md §12).
/// <para>
/// <b>열거는 여전히 다 내준다.</b> <c>FileSystemFolderSource</c> 는 <c>AttributesToSkip = 0</c>
/// 이고 "보여줄지 말지는 UI 정책" 이라고 적어 놓았다 — 여기가 그 정책이 실제로 적용되는
/// 자리다. 필터를 열거에 넣으면 설정을 바꿀 때마다 폴더를 다시 읽어야 하고, 트리와 목록이
/// 같은 포트를 쓰면서 서로 다른 정책을 요구할 자리가 생긴다.
/// </para>
/// <para>
/// <b>다시 읽는 것은 페인이 정하지 않는다.</b> 설정이 바뀌었을 때 목록 둘과 트리를 다시
/// 읽는 것은 <c>WorkspaceViewModel</c> 의 일이다 (<c>WorkspaceViewModelTests</c>) — 페인은
/// 값을 받아 거르기만 한다.
/// </para>
/// </summary>
public class PaneHiddenItemsTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
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

    private LocationId Folder(params (string Name, FileItemFlags Flags)[] entries)
    {
        var folder = Loc(@"C:\Temp");

        source.Folders[folder] =
        [
            .. entries.Select(entry => new FileItem(
                entry.Name,
                folder.Combine(entry.Name),
                0,
                DateTimeOffset.UnixEpoch,
                entry.Flags)),
        ];

        return folder;
    }

    [Fact]
    public void New_HidesHiddenItems()
    {
        // 탐색기의 기본값과 같다. 켜져 있으면 C:\ 가 $Recycle.Bin 부터 시작한다.
        Assert.False(CreatePane().ShowHiddenItems);
    }

    [Fact]
    public async Task Enumeration_LeavesOutHiddenAndSystemItems()
    {
        var folder = Folder(
            ("보이는.txt", FileItemFlags.None),
            ("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System),
            ("$Recycle.Bin", FileItemFlags.Directory | FileItemFlags.Hidden | FileItemFlags.System),
            ("숨김만.txt", FileItemFlags.Hidden),
            ("시스템만.dat", FileItemFlags.System));
        await using var pane = CreatePane();

        await pane.NavigateAsync(folder);

        // 토글 하나다 — 숨김만 붙은 것과 시스템만 붙은 것이 함께 간다 (사용자 결정).
        Assert.Equal(["보이는.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Enumeration_WhenAsked_ShowsThemAll()
    {
        var folder = Folder(
            ("보이는.txt", FileItemFlags.None),
            ("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System));
        await using var pane = CreatePane();
        pane.ShowHiddenItems = true;

        await pane.NavigateAsync(folder);

        Assert.Equal(2, pane.Items.Count);
    }

    [Fact]
    public async Task StatusText_CountsOnlyWhatIsShown()
    {
        // 상태표시줄이 '항목 2개' 인데 한 줄만 보이면 목록이 잘린 것처럼 보인다.
        var folder = Folder(
            ("보이는.txt", FileItemFlags.None),
            ("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System));
        await using var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal("항목 1개", pane.StatusText);
    }

    [Fact]
    public async Task EmptyAfterFiltering_ReadsAsAnEmptyFolder()
    {
        // 숨김뿐인 폴더는 사용자에게 빈 폴더다. '읽는 중' 이나 오류로 남으면 안 된다.
        var folder = Folder(("desktop.ini", FileItemFlags.Hidden | FileItemFlags.System));
        await using var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Empty, pane.Status);
    }

    // ── 감시 갱신도 같은 정책을 지난다 ────────────────────────────

    [Fact]
    public async Task WatchUpdate_DoesNotSlipAHiddenItemIntoTheList()
    {
        // 열거만 거르고 감시를 놔두면, 폴더에 숨김 파일이 생기는 순간 목록에 나타나고
        // 다음 새로 고침 때까지 남는다. 정책을 지나는 자리가 둘이라는 뜻이다.
        var folder = Folder(("a.txt", FileItemFlags.None));
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        source.Folders[folder].Add(new FileItem(
            "desktop.ini",
            folder.Combine("desktop.ini"),
            0,
            DateTimeOffset.UnixEpoch,
            FileItemFlags.Hidden | FileItemFlags.System));
        source.Folders[folder].Add(new FileItem(
            "b.txt",
            folder.Combine("b.txt"),
            0,
            DateTimeOffset.UnixEpoch,
            FileItemFlags.None));

        watcher.Push(new FolderChange(FolderChangeKind.Added, "desktop.ini"));
        watcher.Push(new FolderChange(FolderChangeKind.Added, "b.txt"));

        // 보이는 쪽이 들어오는 것을 기다린 뒤에 본다 — 숨김이 안 들어온 것을 곧바로 재면
        // 아직 처리되지 않은 것과 구분되지 않는다.
        await WaitAsync(pane, () => pane.Items.Count == 2, "보이는 항목이 목록에 들어온다");

        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task WatchUpdate_WhenAnItemBecomesHidden_TakesItOutOfTheList()
    {
        // 탐색기에서 속성을 바꾸면 알림이 온다. 값은 파일시스템에서 다시 읽으므로
        // (CLAUDE.md §4) 그때 숨김이 붙어 있으면 목록에서 빠져야 한다.
        var folder = Folder(("a.txt", FileItemFlags.None), ("b.txt", FileItemFlags.None));
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        source.Folders[folder][0] = source.Folders[folder][0] with { Flags = FileItemFlags.Hidden };
        watcher.Push(new FolderChange(FolderChangeKind.Changed, "a.txt"));

        await WaitAsync(pane, () => pane.Items.Count == 1, "숨김이 붙은 항목이 목록에서 빠진다");

        Assert.Equal(["b.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task WatchUpdate_WhenShowingHidden_LetsHiddenItemsIn()
    {
        var folder = Folder(("a.txt", FileItemFlags.None));
        await using var pane = CreatePane();
        pane.ShowHiddenItems = true;
        await pane.NavigateAsync(folder);

        source.Folders[folder].Add(new FileItem(
            "desktop.ini",
            folder.Combine("desktop.ini"),
            0,
            DateTimeOffset.UnixEpoch,
            FileItemFlags.Hidden | FileItemFlags.System));

        watcher.Push(new FolderChange(FolderChangeKind.Added, "desktop.ini"));

        await WaitAsync(pane, () => pane.Items.Count == 2, "숨김 항목이 목록에 들어온다");
    }

    /// <summary>
    /// 알림은 감시 루프의 스레드에서 온다 (<c>PaneWatcherTests</c> 와 같은 이유로 복사해 둔다).
    /// </summary>
    private static async Task WaitAsync(PaneViewModel pane, Func<bool> reached, string expectation)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            if (reached())
            {
                signal.TrySetResult();
            }
        }

        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Check();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => Check();

        pane.Items.CollectionChanged += OnItemsChanged;
        pane.PropertyChanged += OnPropertyChanged;

        try
        {
            Check();

            await signal.Task.WaitAsync(Limit);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"{expectation} — {Limit.TotalSeconds}초 안에 일어나지 않았다.");
        }
        finally
        {
            pane.Items.CollectionChanged -= OnItemsChanged;
            pane.PropertyChanged -= OnPropertyChanged;
        }
    }
}
