using System.Globalization;

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
    private readonly FakeTypeNameProvider typeNames = new();
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
        Assert.Throws<ArgumentNullException>(
            () => new PaneViewModel(null!, typeNames, dispatcher, Culture, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentNullException>(
            () => new PaneViewModel(source, null!, dispatcher, Culture, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentNullException>(
            () => new PaneViewModel(source, typeNames, null!, Culture, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentNullException>(
            () => new PaneViewModel(source, typeNames, dispatcher, null!, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentNullException>(
            () => new PaneViewModel(source, typeNames, dispatcher, Culture, null!));
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
        var pane = new PaneViewModel(source, typeNames, dispatcher, Culture, zone);

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
    [InlineData(@"\\server\share")]
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

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneViewModel CreatePane() => new(source, typeNames, dispatcher, Culture, TimeZoneInfo.Utc);

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
