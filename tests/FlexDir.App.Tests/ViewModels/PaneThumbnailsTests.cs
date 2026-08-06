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
/// 페인이 <see cref="ThumbnailRequestScheduler"/> 를 소유하고 폴더 전환·뷰 전환·종료를
/// 그것에 연결한다 (.harness/manual-plan.md §B).
/// <para>
/// 정책 자체는 <see cref="ThumbnailRequestSchedulerTests"/> 가 잰다. 여기서 재는 것은
/// <b>배선</b> 뿐이다 — 스케줄러가 정말 만들어지는가, 크기가 어디서 오는가, 폴더를 옮길 때
/// 이전 폴더의 요청이 끊기는가, 페인을 닫으면 함께 닫히는가.
/// </para>
/// </summary>
public class PaneThumbnailsTests
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

    // ── 크기는 뷰 모드가 정한다 ───────────────────────────────────

    [Theory]
    [InlineData(ViewMode.Details, 16)]
    [InlineData(ViewMode.List, 16)]
    [InlineData(ViewMode.Tiles, 32)]
    [InlineData(ViewMode.LargeIcons, 96)]
    public async Task SetVisibleRange_AsksWithTheSizeOfTheCurrentViewMode(ViewMode mode, int expected)
    {
        // docs/DESIGN.md §2 의 표가 정본이다. View 가 계산해 넘기면 같은 표가 두 계층에 갈린다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);
        await pane.ChangeViewModeCommand.ExecuteAsync(mode);

        pane.SetVisibleRange([.. pane.Items]);
        await pane.ThumbnailWork;

        Assert.Equal(expected, Assert.Single(thumbnails.TypeIconRequests).RequestedSize);
        Assert.Equal(expected, Assert.Single(thumbnails.ThumbnailRequests).RequestedSize);
    }

    [Fact]
    public async Task SetVisibleRange_AfterSwitchingViewMode_AsksAgainWithTheNewSize()
    {
        // 크기가 스케줄러의 캐시 키에 들어간다. 16px 아이콘을 96px 자리에 늘려 쓰면 뿌옇다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        pane.SetVisibleRange([.. pane.Items]);
        await pane.ThumbnailWork;

        await pane.ChangeViewModeCommand.ExecuteAsync(ViewMode.LargeIcons);
        pane.SetVisibleRange([.. pane.Items]);
        await pane.ThumbnailWork;

        Assert.Equal([16, 96], [.. thumbnails.TypeIconRequests.Select(request => request.RequestedSize)]);
    }

    // ── 폴더 전환 ─────────────────────────────────────────────────

    [Fact]
    public async Task NavigateAsync_CancelsTheRequestsOfTheFolderWeLeft()
    {
        // 이전 폴더의 요청이 살아남으면 새 폴더의 행에 옛 그림이 붙는다.
        var first = Folder(@"C:\Temp\A", "a.jpg");
        var second = Folder(@"C:\Temp\B", "b.jpg");
        var gate = new TaskCompletionSource();
        thumbnails.ThumbnailGate = gate.Task;

        var pane = CreatePane();

        try
        {
            await pane.NavigateAsync(first);

            var rows = pane.Items.ToList();
            pane.SetVisibleRange(rows);

            // 썸네일 요청이 관문에 걸려 있는 동안 폴더를 옮긴다.
            await pane.NavigateAsync(second);

            Assert.Equal(1, thumbnails.CancellationsObserved);
            Assert.Null(rows[0].Thumbnail);
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Fact]
    public async Task ReopeningTheSameFolder_DoesNotCancelTheRequestsInFlight()
    {
        // 같은 폴더를 다시 여는 경로가 여럿이다 — 시작할 때의 복원과 활성화 라우팅이 겹치고,
        // F5 도 여기로 온다. 그때 끊으면 그림이 영영 오지 않는다: 항목 인스턴스는 그대로라
        // (MergeItems) View 가 "보이는 것이 바뀌었다" 고 볼 일이 없어 다시 밀지 않는다.
        // 실물에서 시작 폴더의 아이콘이 통째로 비어 있었다.
        var folder = Folder(@"C:\Temp", "a.jpg");
        var gate = new TaskCompletionSource();
        thumbnails.ThumbnailGate = gate.Task;

        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        var rows = pane.Items.ToList();
        pane.SetVisibleRange(rows);

        // 요청이 관문에 걸려 있는 동안 같은 폴더를 다시 연다.
        await pane.NavigateAsync(folder);

        gate.SetResult();
        await pane.ThumbnailWork;

        Assert.Equal(0, thumbnails.CancellationsObserved);
        Assert.NotNull(rows[0].Thumbnail);
    }

    [Fact]
    public async Task NavigateAsync_KeepsTheTypeIconCache()
    {
        // 확장자 아이콘은 폴더와 무관하다. 폴더를 옮길 때마다 다시 물으면 같은 값을 사 온다.
        var first = Folder(@"C:\Temp\A", "a.txt");
        var second = Folder(@"C:\Temp\B", "b.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(first);
        pane.SetVisibleRange([.. pane.Items]);
        await pane.ThumbnailWork;

        await pane.NavigateAsync(second);
        pane.SetVisibleRange([.. pane.Items]);
        await pane.ThumbnailWork;

        Assert.Equal(1, thumbnails.CountTypeIconRequests("txt", isDirectory: false));
    }

    // ── 종료 ─────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ClosesTheScheduler()
    {
        // 상주 프로세스라 창만 닫히고 프로세스는 남는다 (ADR-003) — 그때 진행 중 요청과
        // BGRA 버퍼가 함께 정리돼야 한다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);
        await pane.DisposeAsync();

        pane.SetVisibleRange([.. pane.Items]);

        Assert.Empty(thumbnails.TypeIconRequests);
        Assert.Empty(thumbnails.ThumbnailRequests);
    }

    // ── 인자 ─────────────────────────────────────────────────────

    [Fact]
    public void Ctor_NullThumbnailSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, null!, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc));
    }

    [Fact]
    public void SetVisibleRange_Null_Throws()
    {
        var pane = CreatePane();

        Assert.Throws<ArgumentNullException>(() => pane.SetVisibleRange(null!));
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);
        source.Folders[folder] = [.. names.Select(name => Entry(folder, name))];

        return folder;
    }

    private static FileItem Entry(LocationId folder, string name)
        => new(name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }
}
