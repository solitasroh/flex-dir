using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 페인 간·외부 드래그앤드롭의 드롭 처리 (docs/DESIGN.md §9-1).
/// <para>
/// 드롭 데이터는 경로 문자열 목록이다 — WPF <c>DataObject</c> 가 CF_HDROP 마샬링을
/// 대신하므로 <c>FlexDir.Shell</c> 이 필요 없고, 경로 파싱은 ViewModel 이 한다.
/// 어디에 떨어졌는가(빈 공간 → 현재 폴더 · 폴더 항목 위 → 그 폴더 안)를 폴더로
/// 바꾸는 것은 View 의 일이고, 여기는 그 결과만 받는다.
/// </para>
/// </summary>
public class PaneDropTests
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

    [Fact]
    public async Task DropAsync_CopiesTheParsedPathsIntoTheTargetFolder()
    {
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Src\a.txt", @"C:\Src\b.txt"], Loc(@"C:\Dst"), isMove: false);

        var (sources, destination) = Assert.Single(operations.Copies);
        Assert.Equal([Loc(@"C:\Src\a.txt"), Loc(@"C:\Src\b.txt")], sources);
        Assert.Equal(Loc(@"C:\Dst"), destination);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task DropAsync_WithIsMove_MovesInstead()
    {
        // Shift 드롭이다. 기본이 복사인 것과의 구분은 View 의 수정키 해석이 한다 (§9-1).
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Src\a.txt"], Loc(@"C:\Dst"), isMove: true);

        var (sources, destination) = Assert.Single(operations.Moves);
        Assert.Equal([Loc(@"C:\Src\a.txt")], sources);
        Assert.Equal(Loc(@"C:\Dst"), destination);
        Assert.Empty(operations.Copies);
    }

    [Fact]
    public async Task DropAsync_SkipsThePathsItCannotParse()
    {
        // 드롭 데이터는 외부 앱이 만든다. 못 읽는 경로가 섞여 있어도 나머지는 처리한다 —
        // 전부 거부하면 잘 끌어온 파일까지 버려진다.
        var pane = CreatePane();

        await pane.DropAsync(["docs", @"C:\Src\a.txt"], Loc(@"C:\Dst"), isMove: false);

        var (sources, _) = Assert.Single(operations.Copies);
        Assert.Equal([Loc(@"C:\Src\a.txt")], sources);
    }

    [Fact]
    public async Task DropAsync_WithNothingParseable_DoesNotCallTheOperation()
    {
        var pane = CreatePane();

        await pane.DropAsync(["docs", ""], Loc(@"C:\Dst"), isMove: false);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task DropAsync_EmptyPaths_DoesNothing()
    {
        var pane = CreatePane();

        await pane.DropAsync([], Loc(@"C:\Dst"), isMove: false);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task DropAsync_MoveIntoTheFolderTheItemsAlreadyLiveIn_IsANoOp()
    {
        // 제자리 이동은 아무 일도 아니다 — 그대로 shell 에 넘기면 오류 대화상자가 뜬다
        // (MoveSelectionToAsync 와 같은 판단).
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Dst\a.txt", @"C:\Dst\b.txt"], Loc(@"C:\Dst"), isMove: true);

        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task DropAsync_MoveSkipsOnlyTheItemsAlreadyInTheTargetFolder()
    {
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Dst\a.txt", @"C:\Src\b.txt"], Loc(@"C:\Dst"), isMove: true);

        var (sources, _) = Assert.Single(operations.Moves);
        Assert.Equal([Loc(@"C:\Src\b.txt")], sources);
    }

    [Fact]
    public async Task DropAsync_CopyIntoTheSameFolder_IsAllowed()
    {
        // shell 이 사본을 만드는 정상 조작이다 (CopySelectionToAsync 와 같은 판단).
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Dst\a.txt"], Loc(@"C:\Dst"), isMove: false);

        Assert.Single(operations.Copies);
    }

    [Fact]
    public async Task DropAsync_FailureLandsOnTheStatusBar()
    {
        var target = Loc(@"C:\Dst");
        operations.Failure = new LocationAccessException(LocationErrorKind.AccessDenied, target);
        var pane = CreatePane();

        await pane.DropAsync([@"C:\Src\a.txt"], target, isMove: false);

        Assert.Equal(
            LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, target),
            pane.StatusText);
    }

    [Fact]
    public async Task DropAsync_NullArguments_Throw()
    {
        var pane = CreatePane();

        await Assert.ThrowsAsync<ArgumentNullException>(() => pane.DropAsync(null!, Loc(@"C:\Dst"), isMove: false));
        await Assert.ThrowsAsync<ArgumentNullException>(() => pane.DropAsync([@"C:\Src\a.txt"], null!, isMove: false));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
