using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 주소줄 breadcrumb (docs/DESIGN.md §1 · 목업 <c>.addr .seg</c> · 사용자 결정 2026-08-06).
/// <para>
/// 경로 분해 자체는 <c>PathSegmentsTests</c> 가 잰다. 여기서 재는 것은 <b>배선</b>이다 —
/// 폴더를 옮기면 칸이 따라오는가, 편집 모드가 기본으로 꺼져 있는가, 편집을 시작한 뒤
/// 폴더를 열면 다시 접히는가.
/// </para>
/// </summary>
public class PaneAddressSegmentsTests
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

    [Fact]
    public async Task OpeningAFolder_FillsTheSegments()
    {
        var pane = CreatePane();

        await pane.NavigateAsync(Folder(@"C:\Projects\flex-dir"));

        Assert.Equal(["C:", "Projects", "flex-dir"], pane.AddressSegments.Select(s => s.Name));
    }

    [Fact]
    public async Task MovingToAnotherFolder_ReplacesTheSegments()
    {
        var pane = CreatePane();

        await pane.NavigateAsync(Folder(@"C:\Projects\flex-dir"));
        await pane.NavigateAsync(Folder(@"C:\Temp"));

        Assert.Equal(["C:", "Temp"], pane.AddressSegments.Select(s => s.Name));
    }

    [Fact]
    public void BeforeAnyFolderIsOpen_ThereAreNoSegments()
    {
        Assert.Empty(CreatePane().AddressSegments);
    }

    [Fact]
    public void EditingIsOffByDefault()
    {
        // 기본은 breadcrumb 다. 편집은 Ctrl+L·Alt+D 나 빈 자리 클릭으로 들어간다 (DESIGN §9).
        Assert.False(CreatePane().IsAddressEditing);
    }

    [Fact]
    public async Task OpeningAFolder_LeavesEditing()
    {
        // 주소를 쳐서 이동한 뒤에도 편집 상태로 남으면 breadcrumb 이 영영 보이지 않는다.
        var pane = CreatePane();
        pane.IsAddressEditing = true;

        await pane.NavigateAsync(Folder(@"C:\Temp"));

        Assert.False(pane.IsAddressEditing);
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
            dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    private LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var folder, out var error), $"파싱 실패: {error}");

        source.Folders[folder] = [new FileItem(
            "a.txt", folder.Combine("a.txt"), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None)];

        return folder;
    }
}
