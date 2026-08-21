using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 툴바 오른쪽 끝의 알려진 폴더 메뉴 (docs/PRD-v2.md §알려진 폴더).
/// <para>
/// 재는 것은 <b>판정과 배선</b>이다 — 없는 폴더가 메뉴에서 빠지는가(<c>ToOptions</c>),
/// 언제 묻는가(폴더를 열 때 딱 한 번), 이동이 히스토리에 남는가. 실제 사용자 폴더는
/// <see cref="FakeKnownFolderList"/> 가 대신한다.
/// </para>
/// </summary>
public class PaneKnownFoldersTests
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
    private readonly FakeKnownFolderList knownFolders = new();

    [Fact]
    public void ToOptions_DropsFoldersWithoutALocation()
    {
        // 이 기계에 없는 폴더다. 눌러도 갈 곳이 없는 항목은 메뉴에 올리지 않는다.
        var desktop = Location(@"C:\Users\Me\Desktop");

        var options = PaneViewModel.ToOptions(
        [
            new KnownFolder(KnownFolderKind.Home, "홈", null),
            new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", desktop),
            new KnownFolder(KnownFolderKind.Documents, "문서", null),
        ]);

        Assert.Equal([new KnownFolderOption("바탕 화면", desktop)], options);
    }

    [Fact]
    public void ToOptions_KeepsTheInputOrder()
    {
        // 순서의 정본은 KnownFolderKind 선언 순서다. 라벨을 정렬하면 정본이 둘이 된다 —
        // 가나다순이면 뒤집힐 라벨로 그대로인지 본다.
        var pictures = Location(@"C:\Users\Me\Pictures");
        var downloads = Location(@"C:\Users\Me\Downloads");

        var options = PaneViewModel.ToOptions(
        [
            new KnownFolder(KnownFolderKind.Downloads, "히읗", downloads),
            new KnownFolder(KnownFolderKind.Pictures, "가나다", pictures),
        ]);

        Assert.Equal(
            [new KnownFolderOption("히읗", downloads), new KnownFolderOption("가나다", pictures)],
            options);
    }

    [Fact]
    public void ToOptions_WhenEveryFolderIsMissing_YieldsAnEmptyList()
    {
        var options = PaneViewModel.ToOptions(
        [
            new KnownFolder(KnownFolderKind.Home, "홈", null),
            new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", null),
            new KnownFolder(KnownFolderKind.Documents, "문서", null),
            new KnownFolder(KnownFolderKind.Downloads, "다운로드", null),
            new KnownFolder(KnownFolderKind.Pictures, "사진", null),
        ]);

        Assert.NotNull(options);
        Assert.Empty(options);
    }

    [Fact]
    public void ToOptions_WhenEveryFolderExists_YieldsAllFive()
    {
        var folders = new[]
        {
            new KnownFolder(KnownFolderKind.Home, "홈", Location(@"C:\Users\Me")),
            new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", Location(@"C:\Users\Me\Desktop")),
            new KnownFolder(KnownFolderKind.Documents, "문서", Location(@"C:\Users\Me\Documents")),
            new KnownFolder(KnownFolderKind.Downloads, "다운로드", Location(@"C:\Users\Me\Downloads")),
            new KnownFolder(KnownFolderKind.Pictures, "사진", Location(@"C:\Users\Me\Pictures")),
        };

        var options = PaneViewModel.ToOptions(folders);

        Assert.Equal(
            [.. folders.Select(folder => new KnownFolderOption(folder.Label, folder.Location!))],
            options);
    }

    [Fact]
    public async Task WithoutThePort_OptionsStayEmpty_AndThePaneStillWorks()
    {
        // 포트는 선택 주입이다 (driveSpace 와 같은 자리). 주지 않은 기존 페인 테스트
        // 수백 개가 그대로 돌아야 한다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

        await pane.NavigateAsync(folder);
        await pane.KnownFoldersWork;

        Assert.Empty(pane.KnownFolderOptions);
        Assert.Single(pane.Items);
    }

    [Fact]
    public async Task OpeningAFolder_FillsTheOptions_AndRaisesTheChange()
    {
        var home = Location(@"C:\Users\Me");
        knownFolders.Folders.Add(new KnownFolder(KnownFolderKind.Home, "홈", home));
        knownFolders.Folders.Add(new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", null));
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        var changed = new List<string?>();
        pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await pane.NavigateAsync(folder);
        await pane.KnownFoldersWork;

        Assert.Equal([new KnownFolderOption("홈", home)], pane.KnownFolderOptions);

        // 알림이 없으면 WPF 바인딩은 런타임 조회라 메뉴가 조용히 영원히 비어 있다.
        Assert.Contains(nameof(PaneViewModel.KnownFolderOptions), changed);
    }

    [Fact]
    public async Task NavigatingAgain_DoesNotAskThePortTwice()
    {
        // 알려진 폴더는 프로세스가 사는 동안 바뀌지 않고 이 앱은 상주다 (ADR-003).
        // 탐색마다 물으면 폴더를 열 때마다 저장소 조회가 하나씩 더 붙는다.
        knownFolders.Folders.Add(new KnownFolder(KnownFolderKind.Home, "홈", Location(@"C:\Users\Me")));
        var first = Folder(@"C:\Temp", "a.txt");
        var second = Folder(@"C:\Other", "b.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(first);
        await pane.KnownFoldersWork;
        await pane.NavigateAsync(second);
        await pane.KnownFoldersWork;

        Assert.Equal(1, knownFolders.Asked);
    }

    [Fact]
    public async Task WhenThePortThrows_TheFolderStillOpens_AndOptionsStayEmpty()
    {
        // 알려진 폴더를 못 읽는 것이 폴더를 못 여는 사건이 되면 안 된다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane(new ThrowingKnownFolderList());

        await pane.NavigateAsync(folder);
        await pane.KnownFoldersWork;

        Assert.Single(pane.Items);
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Empty(pane.KnownFolderOptions);
    }

    [Fact]
    public async Task TheCommand_NavigatesThere_AndRecordsHistory()
    {
        var start = Folder(@"C:\Temp", "a.txt");
        var home = Folder(@"C:\Users\Me", "h.txt");
        knownFolders.Folders.Add(new KnownFolder(KnownFolderKind.Home, "홈", home));
        var pane = CreatePane();
        await pane.NavigateAsync(start);
        await pane.KnownFoldersWork;
        var option = Assert.Single(pane.KnownFolderOptions);

        await pane.OpenKnownFolderCommand.ExecuteAsync(option);

        // NavigateAsync(LocationId) 를 지났다는 증거 — 뒤로가기가 원래 자리로 돌아온다.
        Assert.Equal(home, pane.CurrentLocation);
        Assert.True(pane.CanGoBack);
    }

    [Fact]
    public async Task TheCommand_WithNull_DoesNothing()
    {
        var start = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(start);

        await pane.OpenKnownFolderCommand.ExecuteAsync(null);

        Assert.Equal(start, pane.CurrentLocation);
        Assert.False(pane.CanGoBack);
    }

    private PaneViewModel CreatePane(IKnownFolderList? port = null)
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus, knownFolders: port ?? knownFolders);

    private LocationId Folder(string path, string name)
    {
        var folder = Location(path);

        source.Folders[folder] = [new FileItem(
            name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None)];

        return folder;
    }

    private static LocationId Location(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }

    /// <summary>계약(던지지 않는다)을 어긴 구현체. 새는 예외가 폴더 열기를 무너뜨리지 않는지 본다.</summary>
    private sealed class ThrowingKnownFolderList : IKnownFolderList
    {
        public ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken ct)
            => throw new InvalidOperationException("계약 위반");
    }
}
