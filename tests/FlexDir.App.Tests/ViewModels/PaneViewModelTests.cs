using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Errors;
using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 페인 하나가 폴더를 열고 목록을 점진적으로 채운다.
/// <para>
/// 중간 상태(열거 중)를 보려면 열거가 아직 끝나지 않은 순간이 필요하므로
/// <see cref="FakeFolderSource.YieldDelayMilliseconds"/> 를 쓴다. 지연은 단정문보다 훨씬
/// 길게 잡는다 — 재는 것은 시간이 아니라 "첫 배치 전에 목록이 비지 않는가" 다.
/// </para>
/// </summary>
public class PaneViewModelTests
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

    // ── 초기 상태 ─────────────────────────────────────────────────

    [Fact]
    public void New_HasNothingOpen()
    {
        var pane = CreatePane();

        Assert.Null(pane.CurrentLocation);
        Assert.Equal(string.Empty, pane.AddressText);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(string.Empty, pane.StatusText);
        Assert.Empty(pane.Items);
        Assert.False(pane.CanGoBack);
        Assert.False(pane.CanGoForward);
        Assert.False(pane.CanGoUp);
    }

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            null!, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, null!, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, null!, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, null!, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, null!, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, null!, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, null!, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, null!, Culture, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, null!, TimeZoneInfo.Utc, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, null!, contextMenus));
        Assert.Throws<ArgumentNullException>(() => new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, null!));
    }

    // ── 목록 채우기 ───────────────────────────────────────────────

    [Fact]
    public async Task NavigateAsync_ThreeItems_FillsTheListAndGoesIdle()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(3, pane.Items.Count);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(3, Culture), pane.StatusText);
        Assert.Equal(folder, pane.CurrentLocation);
        Assert.Equal(@"C:\Temp", pane.AddressText);
    }

    [Fact]
    public async Task NavigateAsync_EmptyFolder_ReportsEmptyAndKeepsTheListArea()
    {
        var folder = Folder(@"C:\Temp\Empty");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Empty(pane.Items);
        Assert.Equal(PaneStatus.Empty, pane.Status);
        Assert.Equal(StatusSummary.Empty, pane.StatusText);
        Assert.Equal(folder, pane.CurrentLocation);
    }

    [Fact]
    public async Task NavigateAsync_SortsDirectoriesFirstThenNamesNaturally()
    {
        // 배치가 1개 + 4개로 끊기므로 열거 종료 시의 완전 정렬까지 함께 본다.
        var folder = Folder(@"C:\Temp", "b.txt", "a10.txt", "a2.txt", @"Zip\", @"Alpha\");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(
            ["Alpha", "Zip", "a2.txt", "a10.txt", "b.txt"],
            pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task NavigateAsync_EmptyFolderAfterAnother_DropsThePreviousRows()
    {
        // 이전 폴더 항목이 남으면 잘못된 폴더의 내용으로 보인다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var empty = Folder(@"C:\Temp\Empty");
        var pane = CreatePane();

        await pane.NavigateAsync(docs);
        await pane.NavigateAsync(empty);

        Assert.Empty(pane.Items);
        Assert.Equal(PaneStatus.Empty, pane.Status);
    }

    [Fact]
    public async Task NavigateAsync_KeepsThePreviousRowsUntilTheFirstBatchArrives()
    {
        // 먼저 비우면 폴더 전환마다 빈 화면이 번쩍인다 (docs/UI_GUIDE.md §상태 표현).
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);

        source.YieldDelayMilliseconds = 50;
        var pending = pane.NavigateAsync(pics);

        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(PaneStatus.Enumerating, pane.Status);

        await pending;

        Assert.Equal(["p.jpg"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task NavigateAsync_WhileEnumerating_ShowsTheEnumeratingSummary()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        source.YieldDelayMilliseconds = 50;
        var pane = CreatePane();

        var pending = pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Enumerating, pane.Status);
        Assert.Equal(StatusSummary.ForEnumerating(0, Culture), pane.StatusText);

        await pending;

        Assert.Equal(StatusSummary.ForItems(3, Culture), pane.StatusText);
    }

    [Fact]
    public async Task NavigateAsync_WhileEnumerating_DoesNotMixTheOldFolderRows()
    {
        var docs = Folder(@"C:\Temp\Docs", [.. Enumerable.Range(0, 300).Select(index => $"doc{index}.txt")]);
        var pics = Folder(@"C:\Temp\Pics", "p1.jpg", "p2.jpg");
        source.YieldDelayMilliseconds = 2;
        var pane = CreatePane();

        var slow = pane.NavigateAsync(docs);
        await pane.NavigateAsync(pics);
        await slow;

        Assert.Equal(["p1.jpg", "p2.jpg"], pane.Items.Select(row => row.Name));
        Assert.Equal(pics, pane.CurrentLocation);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
    }

    [Fact]
    public async Task Items_AreOnlyTouchedInsideTheUiDispatcher()
    {
        // CLAUDE.md §3 — 열거는 UI 스레드 밖에서 돌고, 반영만 UI 스레드로 옮긴다.
        var folder = Folder(@"C:\Temp", [.. Enumerable.Range(0, 300).Select(index => $"doc{index}.txt")]);
        var pane = CreatePane();

        var outsideDispatcher = false;
        pane.Items.CollectionChanged += (_, _) =>
        {
            if (!dispatcher.IsInvoking)
            {
                outsideDispatcher = true;
            }
        };

        await pane.NavigateAsync(folder);

        Assert.False(outsideDispatcher, "목록 변경이 UI 디스패처 밖에서 일어났다.");
        Assert.Equal(300, pane.Items.Count);
    }

    // ── 표시 문자열 ───────────────────────────────────────────────

    [Fact]
    public async Task Rows_FormatWithTheInjectedCultureAndTimeZone()
    {
        var folder = Loc(@"C:\Temp");
        var modified = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
        source.Folders[folder] = [new FileItem("report.txt", folder.Combine("report.txt"), 1536, modified, FileItemFlags.None)];

        var zone = TimeZoneInfo.CreateCustomTimeZone("flexdir-test", TimeSpan.FromHours(9), "flexdir-test", "flexdir-test");
        var pane = new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, zone, contextMenus);

        await pane.NavigateAsync(folder);

        var row = Assert.Single(pane.Items);
        Assert.Equal(SizeFormatter.ForItem(row.Item, Culture), row.SizeText);
        Assert.Equal(TimestampFormatter.Format(modified, zone, Culture), row.ModifiedText);

        // 주입한 표준시간대를 실제로 쓴다 — 함수 안에서 TimeZoneInfo.Local 을 읽지 않는다.
        Assert.NotEqual(TimestampFormatter.Format(modified, TimeZoneInfo.Utc, Culture), row.ModifiedText);
    }

    [Fact]
    public async Task Rows_LeaveTheSizeBlankForDirectories()
    {
        // 폴더 용량 계산은 v1 범위 밖이다 (docs/PRD.md §3).
        var folder = Folder(@"C:\Temp", @"Docs\");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        var row = Assert.Single(pane.Items);
        Assert.Equal(string.Empty, row.SizeText);
        Assert.True(row.IsDirectory);
    }

    [Fact]
    public async Task TypeNames_AreLookedUpOncePerExtension()
    {
        // 배치를 넘어서도 한 번이다 — 10개는 1개 + 9개 두 배치로 온다.
        var folder = Folder(@"C:\Temp", [.. Enumerable.Range(0, 10).Select(index => $"doc{index}.txt")]);
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(1, typeNames.CountFor("txt", isDirectory: false));
        Assert.All(pane.Items, row => Assert.Equal("TXT", row.TypeText));
    }

    [Fact]
    public async Task TypeNames_DistinguishDirectoriesFromExtensionlessFiles()
    {
        var folder = Folder(@"C:\Temp", @"Docs\", @"Pics\", "LICENSE", "README");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(1, typeNames.CountFor(string.Empty, isDirectory: true));
        Assert.Equal(1, typeNames.CountFor(string.Empty, isDirectory: false));
        Assert.Equal(["DIR", "DIR", "FILE", "FILE"], pane.Items.Select(row => row.TypeText));
    }

    [Fact]
    public async Task TypeNames_AFailedLookup_LeavesTheColumnBlankAndIsNotRetried()
    {
        // 계약대로 실패한 경우다 — 빈 문자열 (ITypeNameProvider). 재시도하지 않는다:
        // 유형 이름을 못 읽는 폴더에서 파일마다 shell 호출이 나가면 그것만으로 멈춘다
        // (docs/PRD.md §4 — 썸네일 추출 실패와 같은 판단).
        var folder = Folder(@"C:\Temp", [.. Enumerable.Range(0, 10).Select(index => $"doc{index}.txt")]);
        typeNames.UnknownExtensions.Add("txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(1, typeNames.CountFor("txt", isDirectory: false));
        Assert.All(pane.Items, row => Assert.Equal(string.Empty, row.TypeText));
        Assert.Equal(PaneStatus.Idle, pane.Status);
    }

    [Fact]
    public async Task TypeNames_WhenTheLookupThrows_TheFolderStillOpens()
    {
        // 계약은 "실패는 빈 문자열" 이지만 구현체는 COM 위에 선다. 예외가 열거 밖으로 새면
        // Status 가 Enumerating 에 남고, 그러면 RefreshStatusText 가 빠져(오류 사유도 안 나온다)
        // 페인이 '읽는 중' 에서 영구 정지한다 — 회복 경로가 없다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        typeNames.Failure = new InvalidOperationException("shell 이 답하지 않는다.");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);

        // 유형 컬럼만 빈칸이다. 실패도 캐시되므로 파일마다 다시 묻지 않는다.
        Assert.All(pane.Items, row => Assert.Equal(string.Empty, row.TypeText));
        Assert.Equal(1, typeNames.CountFor("txt", isDirectory: false));
    }

    // ── 오류 ──────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateAsync_AccessDenied_KeepsThePathAndEmptiesTheList()
    {
        // docs/PRD.md §4 — 이전 경로로 되돌리지 않는다. 사유는 상태표시줄에.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var locked = Folder(@"C:\Temp\Locked", "secret.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);

        source.FailureInjection = (0, LocationErrorKind.AccessDenied);
        await pane.NavigateAsync(locked);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Equal(locked, pane.CurrentLocation);
        Assert.Equal(@"C:\Temp\Locked", pane.AddressText);
        Assert.Empty(pane.Items);
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, locked), pane.StatusText);
    }

    [Fact]
    public async Task NavigateAsync_FailureMidEnumeration_DropsTheRowsAlreadyShown()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        source.FailureInjection = (1, LocationErrorKind.AccessDenied);
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Empty(pane.Items);
    }

    [Fact]
    public async Task NavigateAsync_WhenEnumerationThrowsOffContract_DoesNotStayReading()
    {
        // 열거 실패는 LocationAccessException 이어야 하지만 (IFolderSource) 구현체는
        // FindFirstFileExW P/Invoke 위에 선다. 예외가 밖으로 새면 Status 가 Enumerating 에 남고,
        // 그 상태에서는 RefreshStatusText 도 물러나므로 사유조차 나오지 않는다 — 페인이
        // '읽는 중' 에서 영구 정지하고 회복 경로가 없다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        source.ContractViolation = (0, new IOException("핸들이 유효하지 않다."));
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Empty(pane.Items);

        // 사유 문구가 없는 예외다. 분류할 수 없는 실패의 문구는 이미 하나 있다.
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.Unknown, folder), pane.StatusText);

        // 경로는 되돌리지 않는다 (docs/PRD.md §4).
        Assert.Equal(folder, pane.CurrentLocation);
    }

    [Fact]
    public async Task NavigateAsync_OffContractFailureMidEnumeration_DropsTheRowsAlreadyShown()
    {
        // 계약 예외 경로는 목록을 비운다 (NavigateAsync_FailureMidEnumeration_...). 예외 종류로
        // 가르면 이쪽만 남아, 읽다 만 폴더의 일부가 완전한 목록처럼 보인다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        source.ContractViolation = (1, new IOException("핸들이 유효하지 않다."));
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Empty(pane.Items);
    }

    [Fact]
    public async Task NavigateAsync_NotFound_MovesUpOneLevelAndKeepsTheReason()
    {
        // docs/PRD.md §4 — 경로가 사라짐: 상위 폴더로 자동 이동하고 사유를 알린다.
        var parent = Folder(@"C:\Temp", "a.txt");
        var gone = Loc(@"C:\Temp\Gone");
        var pane = CreatePane();

        await pane.NavigateAsync(gone);

        Assert.Equal(parent, pane.CurrentLocation);
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, gone), pane.StatusText);
    }

    [Fact]
    public async Task NavigateAsync_NotFound_RecordsTheAutomaticMoveInHistory()
    {
        var parent = Folder(@"C:\Temp", "a.txt");
        var gone = Loc(@"C:\Temp\Gone");
        var pane = CreatePane();

        await pane.NavigateAsync(gone);

        // 사라진 폴더도 기록에 남는다 — 뒤로가 그 시도로 돌아간다.
        Assert.True(pane.CanGoBack);

        // 여전히 없으므로 다시 상위로 올라오고, 기록이 늘어나지도 않는다.
        await pane.GoBackAsync();

        Assert.Equal(parent, pane.CurrentLocation);
        Assert.True(pane.CanGoBack);
        Assert.False(pane.CanGoForward);
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, gone), pane.StatusText);
    }

    [Fact]
    public async Task NavigateAsync_NotFound_DoesNotChainUpMoreThanOneLevel()
    {
        // 연쇄로 올라가면 사용자가 어디로 갔는지 모른다. 부모도 없으면 그 자리에서 멈춘다.
        var gone = Loc(@"C:\Temp\Gone");
        var parent = Loc(@"C:\Temp");
        var pane = CreatePane();

        await pane.NavigateAsync(gone);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Equal(parent, pane.CurrentLocation);
        Assert.Empty(pane.Items);
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, parent), pane.StatusText);
    }

    [Fact]
    public async Task NavigateAsync_NotFoundAtDriveRoot_StopsWithoutMoving()
    {
        var root = Loc(@"C:\");
        var pane = CreatePane();

        await pane.NavigateAsync(root);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Equal(root, pane.CurrentLocation);
        Assert.Empty(pane.Items);
        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, root), pane.StatusText);
        Assert.Single(source.EnumerateCalls);
    }

    // ── 주소 문자열 ───────────────────────────────────────────────

    [Fact]
    public async Task NavigateAsync_Address_IsParsedAndNormalized()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync("C:/Temp/");

        Assert.Equal(folder, pane.CurrentLocation);
        Assert.Equal(@"C:\Temp", pane.AddressText);
        Assert.Single(pane.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("docs")]
    [InlineData(@"C:\Temp\a<b")]
    [InlineData(@"C:\Temp\a:b")]
    [InlineData(@"\\")]           // 서버 이름이 없다 — UNC 자체는 이제 유효하다 (PRD-v2 §5 N-1)
    public async Task NavigateAsync_BadAddress_ReportsErrorWithoutThrowing(string address)
    {
        var pane = CreatePane();

        await pane.NavigateAsync(address);

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.NotEqual(string.Empty, pane.StatusText);
        Assert.Null(pane.CurrentLocation);

        // 파싱에 실패한 것은 열 수 있는 위치가 아니다 — 열거를 시도조차 하지 않는다.
        Assert.Empty(source.EnumerateCalls);
    }

    [Fact]
    public async Task NavigateAsync_BadAddress_LeavesTheOpenFolderAlone()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.NavigateAsync("docs");

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Equal(folder, pane.CurrentLocation);

        // 열지 않았으므로 보이는 목록은 여전히 맞다.
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 뒤로 · 앞으로 · 상위로 ────────────────────────────────────

    [Fact]
    public async Task GoBack_And_GoForward_RereadTheFolderAndTrackWhatIsPossible()
    {
        var root = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(root);
        Assert.False(pane.CanGoBack);
        Assert.False(pane.CanGoForward);
        Assert.True(pane.CanGoUp);

        await pane.NavigateAsync(docs);
        Assert.True(pane.CanGoBack);
        Assert.False(pane.CanGoForward);

        await pane.GoBackAsync();
        Assert.Equal(root, pane.CurrentLocation);
        Assert.Equal(["Docs"], pane.Items.Select(row => row.Name));
        Assert.False(pane.CanGoBack);
        Assert.True(pane.CanGoForward);

        await pane.GoForwardAsync();
        Assert.Equal(docs, pane.CurrentLocation);
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
        Assert.True(pane.CanGoBack);
        Assert.False(pane.CanGoForward);
    }

    [Fact]
    public async Task GoUpAsync_OpensTheParentAndRecordsIt()
    {
        var parent = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);

        await pane.GoUpAsync();

        Assert.Equal(parent, pane.CurrentLocation);
        Assert.Equal(["Docs"], pane.Items.Select(row => row.Name));

        // 규칙: 상위로 간 뒤에는 뒤로가 자식으로 돌아간다.
        Assert.True(pane.CanGoBack);
        await pane.GoBackAsync();
        Assert.Equal(docs, pane.CurrentLocation);
    }

    [Fact]
    public async Task GoUpAsync_AtDriveRoot_DoesNothing()
    {
        var root = Folder(@"C:\", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(root);

        Assert.False(pane.CanGoUp);
        await pane.GoUpAsync();

        Assert.Equal(root, pane.CurrentLocation);
        Assert.Single(source.EnumerateCalls);
    }

    [Fact]
    public async Task GoBackAsync_WithNothingBehind_DoesNothing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.GoBackAsync();
        await pane.GoForwardAsync();

        Assert.Equal(folder, pane.CurrentLocation);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Single(source.EnumerateCalls);
    }

    // ── 네비게이션 커맨드 ─────────────────────────────────────────
    // InputBindings 는 ICommand 만 물 수 있다 (docs/DESIGN.md §9 키보드 맵).
    // 메서드와 커맨드가 같은 히스토리를 움직여야 한다.

    [Fact]
    public async Task NavigationCommands_DriveTheSameHistoryAsTheMethods()
    {
        var root = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(root);
        await pane.NavigateAsync(docs);

        await pane.GoBackCommand.ExecuteAsync(null);
        Assert.Equal(root, pane.CurrentLocation);

        await pane.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(docs, pane.CurrentLocation);

        await pane.GoUpCommand.ExecuteAsync(null);
        Assert.Equal(root, pane.CurrentLocation);
    }

    [Fact]
    public async Task RefreshCommand_RereadsTheCurrentFolder()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.RefreshCommand.ExecuteAsync(null);

        Assert.Equal([folder, folder], source.EnumerateCalls);
        Assert.Equal(folder, pane.CurrentLocation);
    }

    // ── 새로 고침 ─────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_RereadsTheFolderWithoutTouchingHistory()
    {
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(a);
        await pane.NavigateAsync(b);

        await pane.RefreshAsync();

        Assert.Equal(b, pane.CurrentLocation);
        Assert.False(pane.CanGoForward);
        Assert.True(pane.CanGoBack);

        // 뒤로 한 번에 A 로 간다 — B 가 두 번 쌓이지 않았다.
        await pane.GoBackAsync();
        Assert.Equal(a, pane.CurrentLocation);
        Assert.False(pane.CanGoBack);

        // 그리고 새로 고침은 실제로 다시 읽었다.
        Assert.Equal([a, b, b, a], source.EnumerateCalls);
    }

    [Fact]
    public async Task RefreshAsync_BeforeAnyNavigation_DoesNothing()
    {
        var pane = CreatePane();

        await pane.RefreshAsync();

        Assert.Null(pane.CurrentLocation);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Empty(source.EnumerateCalls);
    }

    // ── 알림 ──────────────────────────────────────────────────────

    [Fact]
    public async Task NavigateAsync_RaisesPropertyChangedForEverythingTheViewBindsTo()
    {
        var root = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(root);

        var changed = new List<string?>();
        pane.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await pane.NavigateAsync(docs);

        Assert.Contains(nameof(PaneViewModel.CurrentLocation), changed);
        Assert.Contains(nameof(PaneViewModel.AddressText), changed);
        Assert.Contains(nameof(PaneViewModel.Status), changed);
        Assert.Contains(nameof(PaneViewModel.StatusText), changed);
        Assert.Contains(nameof(PaneViewModel.CanGoBack), changed);
        Assert.Contains(nameof(PaneViewModel.CanGoForward), changed);
        Assert.Contains(nameof(PaneViewModel.CanGoUp), changed);
    }

    // ── 선택 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Selection_SurvivesAReorderOfTheList()
    {
        // 정렬 전환이 하는 일은 목록 재배치다 (PaneViewModel.SortItems 도 ReplaceAll 이다).
        // 선택은 이름으로 보관하므로 순서가 바뀌어도 같은 항목이 선택돼 있다
        // (docs/UI_GUIDE.md §원칙 4). 정렬 명령 자체는 다음 step 이다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");
        var summary = pane.StatusText;

        pane.Items.ReplaceAll(pane.Items.Reverse().ToList());

        Assert.Equal(["c.txt", "b.txt", "a.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(["b.txt"], pane.Selection.SelectedNames);
        Assert.Equal("b.txt", pane.Selection.Anchor);
        Assert.Equal(summary, pane.StatusText);
    }

    [Fact]
    public async Task Selection_SurvivesARefreshThatRebuildsEveryRow()
    {
        // 갱신은 FileItemViewModel 인스턴스를 전부 교체한다 — 참조로 보관했다면 여기서 끊긴다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");
        pane.Selection.Toggle("c.txt");

        await pane.RefreshAsync();

        Assert.Equal(2, pane.Selection.Count);
        Assert.True(pane.Selection.IsSelected("b.txt"));
        Assert.True(pane.Selection.IsSelected("c.txt"));
    }

    [Fact]
    public async Task Selection_DropsTheNamesThatDisappearedFromTheFolder()
    {
        // 남기면 상태표시줄의 선택 개수가 실제와 어긋난다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");
        pane.Selection.Toggle("c.txt");

        Folder(@"C:\Temp", "a.txt", "b.txt");
        await pane.RefreshAsync();

        Assert.Equal(["b.txt"], pane.Selection.SelectedNames);
        Assert.Equal(StatusSummary.ForSelection(2, 1, 1024, Culture), pane.StatusText);
    }

    [Fact]
    public async Task Selection_IsClearedWhenTheFolderChanges()
    {
        // 다른 폴더의 이름이 남으면 새 폴더의 엉뚱한 항목이 선택된다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt", "b.txt");
        var pics = Folder(@"C:\Temp\Pics", "a.txt", "p.jpg");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);
        pane.Selection.SelectSingle("a.txt");

        await pane.NavigateAsync(pics);

        Assert.Equal(0, pane.Selection.Count);
        Assert.Null(pane.Selection.Anchor);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
    }

    [Fact]
    public async Task Selection_IsClearedWhenGoingBack()
    {
        var root = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(root);
        await pane.NavigateAsync(docs);
        pane.Selection.SelectSingle("a.txt");

        await pane.GoBackAsync();

        Assert.Equal(0, pane.Selection.Count);
    }

    // ── 선택 요약 ─────────────────────────────────────────────────

    [Fact]
    public async Task StatusText_WithNothingSelected_CountsTheItems()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);

        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
    }

    [Fact]
    public async Task StatusText_WithASelection_SumsTheSelectedSizes()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        pane.Selection.SelectSingle("a.txt");
        Assert.Equal(StatusSummary.ForSelection(3, 1, 1024, Culture), pane.StatusText);

        pane.Selection.Toggle("c.txt");
        Assert.Equal(StatusSummary.ForSelection(3, 2, 2048, Culture), pane.StatusText);

        pane.Selection.Clear();
        Assert.Equal(StatusSummary.ForItems(3, Culture), pane.StatusText);
    }

    [Fact]
    public async Task StatusText_CountsDirectoriesAsZeroBytes()
    {
        // SizeFormatter.ForItem 과 같은 규칙이다 — 폴더 용량 계산은 v1 범위 밖이다.
        var folder = Folder(@"C:\Temp", @"Docs\", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        pane.Selection.SelectSingle("Docs");
        Assert.Equal(StatusSummary.ForSelection(2, 1, 0, Culture), pane.StatusText);

        pane.Selection.Toggle("a.txt");
        Assert.Equal(StatusSummary.ForSelection(2, 2, 1024, Culture), pane.StatusText);
    }

    [Fact]
    public async Task StatusText_WhileEnumerating_PrefersTheProgress()
    {
        // 열거 중에는 총 개수가 아직 확정되지 않았다 — 선택 요약을 내면 거짓말이 된다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");

        source.YieldDelayMilliseconds = 50;
        var pending = pane.RefreshAsync();

        Assert.Equal(PaneStatus.Enumerating, pane.Status);
        Assert.Equal(StatusSummary.ForEnumerating(0, Culture), pane.StatusText);

        // 열거 중에 선택이 바뀌어도 진행 표시가 먼저다.
        pane.Selection.Toggle("b.txt");
        Assert.Equal(StatusSummary.ForEnumerating(0, Culture), pane.StatusText);

        await pending;

        Assert.Equal(StatusSummary.ForSelection(3, 2, 2048, Culture), pane.StatusText);
    }

    [Fact]
    public async Task StatusText_OnError_KeepsTheReason()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        source.FailureInjection = (0, LocationErrorKind.AccessDenied);
        await pane.RefreshAsync();

        var reason = LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, folder);
        Assert.Equal(reason, pane.StatusText);

        // 목록이 비었으므로 남은 선택은 개수가 아니라 사유를 덮지 않아야 한다.
        pane.Selection.SelectSingle("a.txt");
        Assert.Equal(reason, pane.StatusText);
    }

    // ── 탭으로서의 페인 (docs/PRD-v2.md §17) ───────────────────────
    // 탭 하나가 페인 하나다 (ADR-018). 그래서 탭 줄이 그릴 것 — 제목·고정 — 이 여기 산다.

    [Fact]
    public async Task Title_IsTheFolderName()
    {
        var pane = CreatePane();
        await pane.NavigateAsync(Folder(@"C:\Temp\Docs"));

        Assert.Equal("Docs", pane.Title);
    }

    [Fact]
    public void Title_BeforeAnyFolder_IsEmpty()
    {
        // 조립 직후의 페인이다. 탭 줄이 아직 그릴 것이 없다.
        Assert.Equal(string.Empty, CreatePane().Title);
    }

    [Fact]
    public async Task Title_WhenTheUserRenamedTheTab_KeepsTheUserName()
    {
        var pane = CreatePane();
        await pane.NavigateAsync(Folder(@"C:\Temp\Docs"));

        pane.CustomTitle = "일감";

        Assert.Equal("일감", pane.Title);
    }

    [Fact]
    public async Task Title_WithAUserNameAndANewFolder_StillKeepsTheUserName()
    {
        // 사용자가 바꿨으면 그것이 이긴다 (docs/PRD-v2.md §17). 폴더를 옮겼다고 되돌아가면
        // 이름을 바꾼 뜻이 사라진다.
        var pane = CreatePane();
        await pane.NavigateAsync(Folder(@"C:\Temp\Docs"));
        pane.CustomTitle = "일감";

        await pane.NavigateAsync(Folder(@"C:\Temp\Pics"));

        Assert.Equal("일감", pane.Title);
    }

    [Fact]
    public async Task Title_FollowsTheFolder()
    {
        var pane = CreatePane();
        var changed = 0;
        pane.PropertyChanged += (_, args) => changed += args.PropertyName == nameof(PaneViewModel.Title) ? 1 : 0;

        await pane.NavigateAsync(Folder(@"C:\Temp\Docs"));

        Assert.Equal("Docs", pane.Title);
        Assert.True(changed > 0, "탭 줄이 제목을 다시 그릴 근거가 있어야 한다");
    }

    [Fact]
    public void Title_OfABackgroundTab_ComesFromTheLocationItHasNotOpenedYet()
    {
        // 복원된 배경 탭은 위치만 들고 있다 (docs/PRD-v2.md §17) — 열지 않았다고 탭 줄에
        // 빈 이름이 서면 어느 탭인지 고를 수 없다.
        var pane = CreatePane();

        pane.PendingLocation = Loc(@"C:\Temp\Docs");

        Assert.Equal("Docs", pane.Title);
        Assert.Null(pane.CurrentLocation);
    }

    [Fact]
    public void PendingLocation_DoesNotEnumerate()
    {
        // 이것이 cold start 를 지키는 자리다 (ADR-018) — 위치를 실었다고 열거가 나가면
        // 시작할 때 탭 수만큼 저장소를 두드린다.
        var pane = CreatePane();

        pane.PendingLocation = Folder(@"C:\Temp\Docs", "a.txt");

        Assert.Empty(source.EnumerateCalls);
        Assert.Empty(pane.Items);
    }

    [Fact]
    public void IsPinned_DefaultsToFalse()
    {
        Assert.False(CreatePane().IsPinned);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>폴더를 등록한다. 이름이 <c>\</c> 로 끝나면 디렉터리다.</summary>
    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);
        source.Folders[folder] = [.. names.Select(name => Entry(folder, name))];
        return folder;
    }

    private static FileItem Entry(LocationId folder, string name)
    {
        var isDirectory = name.EndsWith('\\');
        var bare = isDirectory ? name[..^1] : name;

        return new FileItem(
            bare,
            folder.Combine(bare),
            isDirectory ? 0 : 1024,
            DateTimeOffset.UnixEpoch,
            isDirectory ? FileItemFlags.Directory : FileItemFlags.None);
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
