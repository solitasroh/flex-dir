using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 상태표시줄 오른쪽 끝의 여유 용량 (docs/DESIGN.md §1 · 목업 <c>.free</c> ·
/// 사용자 결정 2026-08-06).
/// <para>
/// 문구 자체는 <c>StatusSummaryTests</c> 가 잰다. 여기서 재는 것은 <b>배선</b>이다 —
/// 폴더를 열 때 묻는가, 모를 때 무엇을 내는가, 포트를 주지 않으면 조용한가.
/// </para>
/// </summary>
public class PaneFreeSpaceTests
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
    private readonly FakeDriveSpace driveSpace = new();

    [Fact]
    public async Task OpeningAFolder_ShowsTheFreeSpaceOfItsVolume()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        driveSpace.Spaces[folder] = new DriveSpace(213L * 1024 * 1024 * 1024, 512L * 1024 * 1024 * 1024);
        var pane = CreatePane();

        await pane.NavigateAsync(folder);
        await pane.FreeSpaceWork;

        Assert.Equal("여유 공간 213.0 GB", pane.FreeSpaceText);
    }

    [Fact]
    public async Task WhenTheVolumeIsUnknown_NothingIsShown()
    {
        // "여유 공간 0 B" 는 디스크가 꽉 찼다는 뜻이 된다. 모를 때는 자리를 비운다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.NavigateAsync(folder);
        await pane.FreeSpaceWork;

        Assert.Equal(string.Empty, pane.FreeSpaceText);
    }

    [Fact]
    public async Task MovingToAnotherFolder_AsksAgain()
    {
        // 드라이브가 바뀌면 답도 바뀐다. 한 번 읽고 캐시하면 D: 에서 C: 의 값이 남는다.
        var first = Folder(@"C:\Temp", "a.txt");
        var second = Folder(@"C:\Other", "b.txt");
        driveSpace.Spaces[first] = new DriveSpace(1L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024);
        driveSpace.Spaces[second] = new DriveSpace(5L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024);
        var pane = CreatePane();

        await pane.NavigateAsync(first);
        await pane.FreeSpaceWork;
        await pane.NavigateAsync(second);
        await pane.FreeSpaceWork;

        Assert.Equal([first, second], driveSpace.Asked);
        Assert.Equal("여유 공간 5.0 GB", pane.FreeSpaceText);
    }

    [Fact]
    public async Task WithoutThePort_ThePaneStillWorks_AndSaysNothing()
    {
        // 포트는 선택 주입이다 (TimeProvider 와 같은 자리). 주지 않은 테스트 수백 개가
        // 그대로 돌아야 한다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = new PaneViewModel(
            source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

        await pane.NavigateAsync(folder);
        await pane.FreeSpaceWork;

        Assert.Equal(string.Empty, pane.FreeSpaceText);
        Assert.Single(pane.Items);
    }

    [Fact]
    public async Task AFolderThatCannotBeOpened_ShowsNoFreeSpace()
    {
        // 열지 못한 폴더의 드라이브 용량을 띄우면 오류 문구 옆에 멀쩡한 값이 나란히 선다.
        var folder = Folder(@"C:\Temp", "a.txt");
        driveSpace.Spaces[folder] = new DriveSpace(1L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024);
        source.FailureInjection = (0, Core.Errors.LocationErrorKind.AccessDenied);
        var pane = CreatePane();

        await pane.NavigateAsync(folder);
        await pane.FreeSpaceWork;

        Assert.Equal(string.Empty, pane.FreeSpaceText);
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus, driveSpace: driveSpace);

    private LocationId Folder(string path, string name)
    {
        Assert.True(LocationId.TryParse(path, out var folder, out var error), $"파싱 실패: {error}");

        source.Folders[folder] = [new FileItem(
            name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None)];

        return folder;
    }
}
