using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Presentation;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 썸네일을 <b>요청하는 정책</b>. 그리는 것은 View 의 일이고 얻어오는 것은 Shell 의 일이다.
/// <para>
/// 여기서 재는 것의 절반은 <b>일어나지 않아야 하는 일</b>이다 — 클라우드 자리표시자에는
/// 요청이 가지 않고(내용을 건드리면 다운로드가 트리거된다, docs/SHELL_NOTES.md §열거 함정 3),
/// 확장자마다 조회는 한 번이고(대용량 폴더에서 가장 큰 승리다), 실패한 항목은 다시 묻지
/// 않고(docs/PRD.md §4), 보이는 범위 밖의 버퍼는 들고 있지 않는다(항목당 36KB).
/// </para>
/// </summary>
public class ThumbnailRequestSchedulerTests
{
    /// <summary>Details·목록 뷰의 아이콘 크기 (docs/DESIGN.md §2).</summary>
    private const int SmallSize = 16;

    /// <summary>큰 아이콘 뷰의 썸네일 크기 (docs/DESIGN.md §2).</summary>
    private const int LargeSize = 96;

    private readonly FakeThumbnailSource source = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 형식 아이콘 ───────────────────────────────────────────────

    [Fact]
    public async Task SetVisibleRange_FillsTheTypeIconOfEveryVisibleRow()
    {
        var rows = Rows("a.txt", "b.png", "Docs\\");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 자리를 비워두지 않는다 (docs/UI_GUIDE.md §상태 표현).
        Assert.All(rows, row => Assert.NotNull(row.Icon));
        Assert.All(rows, row => Assert.Equal(FakeThumbnailSource.IconMark, row.Icon!.Pixels[0]));
    }

    [Fact]
    public async Task SetVisibleRange_AsksForTheTypeIconOncePerExtension()
    {
        var rows = Rows([.. Enumerable.Range(0, 10).Select(index => $"photo{index}.jpg")]);
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 같은 확장자 파일이 1000개여도 요청은 1회다 (docs/SHELL_NOTES.md §아이콘).
        Assert.Equal(1, source.CountTypeIconRequests("jpg", isDirectory: false));

        // 사전에서 온 같은 인스턴스여야 한다. 항목마다 새로 만들면 요청만 아낀 셈이다.
        Assert.All(rows, row => Assert.Same(rows[0].Icon, row.Icon));
    }

    [Fact]
    public async Task SetVisibleRange_Directories_ShareOneIconAndAreNeverAskedForAThumbnail()
    {
        var rows = Rows([.. Enumerable.Range(0, 10).Select(index => $"Folder{index}\\")]);
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 디렉터리는 확장자가 없으므로 키 하나로 묶인다.
        Assert.Equal(1, source.CountTypeIconRequests(string.Empty, isDirectory: true));
        Assert.Empty(source.ThumbnailRequests);
        Assert.All(rows, row => Assert.NotNull(row.Icon));
        Assert.All(rows, row => Assert.Null(row.Thumbnail));
    }

    [Fact]
    public async Task SetVisibleRange_WhenTheTypeIconLookupThrows_AsksOnceAndStillLoadsThumbnails()
    {
        var rows = Rows("a.jpg", "b.jpg");
        source.TypeIconFailure = new InvalidOperationException("shell");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 아이콘 조회 실패도 재시도하지 않는다 — 사전에 결과가 남는다.
        Assert.Equal(1, source.CountTypeIconRequests("jpg", isDirectory: false));
        Assert.All(rows, row => Assert.Null(row.Icon));

        // 아이콘이 없다고 썸네일까지 포기하지는 않는다.
        Assert.All(rows, row => Assert.NotNull(row.Thumbnail));
    }

    [Fact]
    public async Task SetVisibleRange_WhenTheViewSizeChanges_AsksForTheIconOfThatSize()
    {
        var rows = Rows("a.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, SmallSize);
        await scheduler.WhenIdle;

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 뷰 모드마다 16·16·32·96 을 쓴다 (docs/DESIGN.md §2). 16px 아이콘을 96px 자리에
        // 그대로 쓰면 뿌옇게 늘어난다 — 크기는 사전 키의 일부다.
        Assert.Equal(2, source.CountTypeIconRequests("jpg", isDirectory: false));
        Assert.Equal([SmallSize, LargeSize], source.TypeIconRequests.Select(request => request.RequestedSize));
        Assert.Equal(LargeSize, rows[0].Icon!.Width);
    }

    // ── 클라우드 자리표시자 ───────────────────────────────────────

    [Theory]
    [InlineData(FileItemFlags.Offline)]
    [InlineData(FileItemFlags.CloudPlaceholder)]
    [InlineData(FileItemFlags.Offline | FileItemFlags.CloudPlaceholder)]
    public async Task SetVisibleRange_ContentAccessRiskyRows_AreNeverAskedForAThumbnail(FileItemFlags flags)
    {
        var risky = Row("cloud.jpg", flags);
        var ordinary = Row("local.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([risky, ordinary], LargeSize);
        await scheduler.WhenIdle;

        // 내용을 건드리면 OneDrive 다운로드가 트리거된다. 스크롤만으로 수 GB 를 내려받게 된다.
        Assert.Equal(0, source.CountThumbnailRequests(risky.Item.Location));
        Assert.Equal(1, source.CountThumbnailRequests(ordinary.Item.Location));

        // 형식 아이콘은 안전하다 — 자리를 비워두지 않는다.
        Assert.NotNull(risky.Icon);
        Assert.Null(risky.Thumbnail);

        // 결론이 났으므로 다시 보여도 묻지 않는다.
        Assert.True(risky.ThumbnailAttempted);
    }

    // ── 썸네일 ───────────────────────────────────────────────────

    [Fact]
    public async Task SetVisibleRange_OrdinaryFile_GetsAThumbnailOfTheRequestedSize()
    {
        var row = Row("a.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);
        await scheduler.WhenIdle;

        Assert.NotNull(row.Thumbnail);
        Assert.Equal(FakeThumbnailSource.ThumbnailMark, row.Thumbnail!.Pixels[0]);
        Assert.Equal(LargeSize, row.Thumbnail.Width);
        Assert.True(row.ThumbnailAttempted);
        Assert.Equal([(row.Item.Location, LargeSize)], source.ThumbnailRequests);
    }

    [Fact]
    public async Task SetVisibleRange_WhenTheSourceHasNoThumbnail_MarksItAttemptedAndLeavesItNull()
    {
        var row = Row("a.jpg");
        source.MissingThumbnails.Add("a.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);
        await scheduler.WhenIdle;

        Assert.Null(row.Thumbnail);
        Assert.NotNull(row.Icon);
        Assert.True(row.ThumbnailAttempted);
    }

    [Fact]
    public async Task SetVisibleRange_WhenTheThumbnailRequestThrows_TheSchedulerKeepsGoing()
    {
        var rows = Rows("a.jpg", "b.jpg");
        source.ThumbnailFailure = new InvalidOperationException("shell");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);
        await scheduler.WhenIdle;

        // 한 항목의 실패가 나머지를 멈추지 않는다.
        Assert.Equal(2, source.ThumbnailRequests.Count);
        Assert.All(rows, row => Assert.True(row.ThumbnailAttempted));
        Assert.All(rows, row => Assert.Null(row.Thumbnail));
        Assert.All(rows, row => Assert.NotNull(row.Icon));

        // 스케줄러는 살아 있다.
        source.ThumbnailFailure = null;
        var later = Row("c.jpg");
        scheduler.SetVisibleRange([later], LargeSize);
        await scheduler.WhenIdle;

        Assert.NotNull(later.Thumbnail);
    }

    [Fact]
    public async Task SetVisibleRange_DoesNotAskAgainForAFailedThumbnail()
    {
        var row = Row("a.jpg");
        var other = Row("b.jpg");
        source.MissingThumbnails.Add("a.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);
        await scheduler.WhenIdle;

        // 스크롤로 나갔다가 다시 들어온다. 실패하는 항목은 계속 실패하고, 스크롤마다 다시
        // 시도하면 워커가 그것으로 막힌다 (docs/PRD.md §4).
        scheduler.SetVisibleRange([other], LargeSize);
        await scheduler.WhenIdle;

        source.MissingThumbnails.Clear();
        scheduler.SetVisibleRange([row], LargeSize);
        await scheduler.WhenIdle;

        Assert.Equal(1, source.CountThumbnailRequests(row.Item.Location));
        Assert.Null(row.Thumbnail);
    }

    // ── 보이는 범위 ───────────────────────────────────────────────

    [Fact]
    public async Task SetVisibleRange_ReleasesTheThumbnailOfRowsThatLeftTheRange()
    {
        var gone = Row("a.jpg");
        var arriving = Row("b.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([gone], LargeSize);
        await scheduler.WhenIdle;
        Assert.NotNull(gone.Thumbnail);

        scheduler.SetVisibleRange([arriving], LargeSize);
        await scheduler.WhenIdle;

        // BGRA 버퍼가 항목당 36KB 다. 10만 항목 폴더를 훑으면 유지할 수 없다.
        Assert.Null(gone.Thumbnail);

        // 아이콘은 남긴다 — 다시 보일 때 빈칸이 되면 안 된다.
        Assert.NotNull(gone.Icon);

        // 시도 표시도 남긴다. 재요청을 막는 것이 그것의 일이다.
        Assert.True(gone.ThumbnailAttempted);
    }

    [Fact]
    public async Task SetVisibleRange_KeepsTheThumbnailOfRowsThatStayVisible()
    {
        var staying = Row("a.jpg");
        var arriving = Row("b.jpg");
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([staying], LargeSize);
        await scheduler.WhenIdle;

        scheduler.SetVisibleRange([staying, arriving], LargeSize);
        await scheduler.WhenIdle;

        Assert.NotNull(staying.Thumbnail);
        Assert.Equal(1, source.CountThumbnailRequests(staying.Item.Location));
        Assert.NotNull(arriving.Thumbnail);
    }

    [Fact]
    public async Task SetVisibleRange_CancelsTheInFlightRequestOfARowThatLeftTheRange()
    {
        var gone = Row("a.jpg");
        var arriving = Row("b.jpg");
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([gone], LargeSize);
        Assert.Equal(1, source.CountThumbnailRequests(gone.Item.Location));

        // 스크롤이 지나갔다 (docs/PRD.md §2 — 스크롤 중 취소).
        scheduler.SetVisibleRange([arriving], LargeSize);

        gate.SetResult();
        await scheduler.WhenIdle;

        Assert.Equal(1, source.CancellationsObserved);
        Assert.Null(gone.Thumbnail);

        // 취소는 시도가 아니다 — 다시 보이면 그때 묻는다.
        Assert.False(gone.ThumbnailAttempted);
        Assert.NotNull(arriving.Thumbnail);
    }

    [Fact]
    public async Task SetVisibleRange_KeepsConcurrentRequestsWithinTheLimit()
    {
        var rows = Rows([.. Enumerable.Range(0, 10).Select(index => $"photo{index}.jpg")]);
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange(rows, LargeSize);

        // shell 호출은 동기 블로킹이고 클라우드·네트워크 항목에서 초 단위로 멈춘다
        // (docs/SHELL_NOTES.md §아이콘 함정 1). 제한이 없으면 워커가 전부 막힌다.
        Assert.Equal(ThumbnailRequestScheduler.MaxConcurrentRequests, source.ThumbnailRequests.Count);

        gate.SetResult();
        await scheduler.WhenIdle;

        Assert.Equal(10, source.ThumbnailRequests.Count);
        Assert.Equal(ThumbnailRequestScheduler.MaxConcurrentRequests, source.PeakConcurrency);
        Assert.All(rows, row => Assert.NotNull(row.Thumbnail));
    }

    [Fact]
    public async Task SetVisibleRange_WithNothingVisible_AsksNothing()
    {
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([], LargeSize);
        await scheduler.WhenIdle;

        Assert.Empty(source.TypeIconRequests);
        Assert.Empty(source.ThumbnailRequests);
    }

    [Fact]
    public async Task SetVisibleRange_AssignsIconAndThumbnailInsideTheDispatcher()
    {
        var row = Row("a.jpg");
        var insideDispatcher = new List<bool>();
        var changed = new List<string?>();

        row.PropertyChanged += (_, args) =>
        {
            changed.Add(args.PropertyName);
            insideDispatcher.Add(dispatcher.IsInvoking);
        };

        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);
        await scheduler.WhenIdle;

        // 바인딩이 값을 보려면 알림이 나가야 하고, 알림은 UI 스레드에서 나가야 한다 (CLAUDE.md §3).
        Assert.Equal(
            [nameof(FileItemViewModel.Icon), nameof(FileItemViewModel.Thumbnail)],
            changed);
        Assert.All(insideDispatcher, inside => Assert.True(inside));
    }

    // ── Reset · Dispose ──────────────────────────────────────────

    [Fact]
    public async Task Reset_CancelsInFlightRequests()
    {
        var row = Row("a.jpg");
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);
        scheduler.Reset();

        gate.SetResult();
        await scheduler.WhenIdle;

        Assert.Equal(1, source.CancellationsObserved);
        Assert.Null(row.Thumbnail);
        Assert.False(row.ThumbnailAttempted);
    }

    [Fact]
    public async Task Reset_KeepsTheTypeIconCache()
    {
        await using var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([Row("a.jpg")], LargeSize);
        await scheduler.WhenIdle;

        // 폴더를 옮긴다. 확장자 아이콘은 폴더와 무관하므로 버릴 이유가 없다.
        scheduler.Reset();

        scheduler.SetVisibleRange([Row("other.jpg")], LargeSize);
        await scheduler.WhenIdle;

        Assert.Equal(1, source.CountTypeIconRequests("jpg", isDirectory: false));
    }

    [Fact]
    public async Task DisposeAsync_CancelsPendingRequests_AndIsIdempotent()
    {
        var row = Row("a.jpg");
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;
        var scheduler = CreateScheduler();

        scheduler.SetVisibleRange([row], LargeSize);

        // 관문은 열리지 않는다. 취소가 요청을 풀지 못하면 여기서 매달린다.
        await scheduler.DisposeAsync();
        await scheduler.DisposeAsync();

        Assert.Equal(1, source.CancellationsObserved);
        Assert.Null(row.Thumbnail);
    }

    [Fact]
    public async Task SetVisibleRange_AfterDispose_DoesNothing()
    {
        var scheduler = CreateScheduler();
        await scheduler.DisposeAsync();

        // 창이 닫히는 중에도 스크롤 이벤트는 온다.
        scheduler.SetVisibleRange([Row("a.jpg")], LargeSize);
        scheduler.Reset();

        Assert.Empty(source.TypeIconRequests);
        Assert.Empty(source.ThumbnailRequests);
    }

    // ── 인자 ─────────────────────────────────────────────────────

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ThumbnailRequestScheduler(null!, dispatcher));
        Assert.Throws<ArgumentNullException>(() => new ThumbnailRequestScheduler(source, null!));
    }

    [Fact]
    public async Task SetVisibleRange_InvalidArguments_Throw()
    {
        await using var scheduler = CreateScheduler();

        Assert.Throws<ArgumentNullException>(() => scheduler.SetVisibleRange(null!, LargeSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.SetVisibleRange([], 0));
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private ThumbnailRequestScheduler CreateScheduler() => new(source, dispatcher);

    /// <summary>이름이 <c>\</c> 로 끝나면 디렉터리다.</summary>
    private static List<FileItemViewModel> Rows(params string[] names)
        => [.. names.Select(name => Row(name))];

    private static FileItemViewModel Row(string name, FileItemFlags flags = FileItemFlags.None)
    {
        var isDirectory = name.EndsWith('\\');
        var bare = isDirectory ? name[..^1] : name;
        var all = isDirectory ? flags | FileItemFlags.Directory : flags;

        var item = new FileItem(
            bare,
            Loc($@"C:\Temp\{bare}"),
            isDirectory ? 0 : 1024,
            DateTimeOffset.UnixEpoch,
            all);

        return new FileItemViewModel(item, "1 KB", "-", "TXT");
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
