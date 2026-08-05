using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// View 가 무는 입력 커맨드 (docs/DESIGN.md §9 · phase B-2).
/// <para>
/// <c>InputBindings</c>(키)와 <c>MouseBinding</c>(클릭)은 <c>ICommand</c> 만 물 수 있으므로
/// <c>MoveFocus</c>·<c>PaneSelection</c> 위에 얇은 커맨드를 씌운다. 판단은 전부 이미 채점된
/// 메서드 안에 있고, 여기서 재는 것은 그 배선이다 — View 에는 로직이 없어야 한다 (CLAUDE.md §2).
/// </para>
/// </summary>
public class PaneInputCommandsTests
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

    // ── 키보드 — MoveFocus 3종 ────────────────────────────────────

    [Fact]
    public async Task MoveFocusToCommand_MovesAndSelects()
    {
        // 표시 순서는 디렉터리 우선이다 — Docs · a.txt · b.txt · c.txt.
        var pane = await OpenAsync();

        pane.MoveFocusToCommand.Execute(FocusMove.Down);      // Docs (부트스트랩)
        pane.MoveFocusToCommand.Execute(FocusMove.Down);

        Assert.Equal("a.txt", pane.FocusedName);
        Assert.Equal(["a.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task ExtendFocusToCommand_GrowsTheRange()
    {
        var pane = await OpenAsync();
        pane.MoveFocusToCommand.Execute(FocusMove.Down);      // Docs — 앵커

        pane.ExtendFocusToCommand.Execute(FocusMove.Down);

        Assert.Equal("a.txt", pane.FocusedName);
        Assert.Equal(2, pane.Selection.Count);
    }

    [Fact]
    public async Task FocusOnlyToCommand_LeavesTheSelectionAlone()
    {
        var pane = await OpenAsync();
        pane.MoveFocusToCommand.Execute(FocusMove.Down);      // Docs 선택

        pane.FocusOnlyToCommand.Execute(FocusMove.Down);

        Assert.Equal("a.txt", pane.FocusedName);
        Assert.Equal(["Docs"], pane.Selection.SelectedNames);
    }

    // ── Ctrl+Space ────────────────────────────────────────────────

    [Fact]
    public async Task ToggleFocusedCommand_TogglesTheFocusedItem()
    {
        var pane = await OpenAsync();
        pane.MoveFocusToCommand.Execute(FocusMove.Down);      // Docs 선택
        pane.FocusOnlyToCommand.Execute(FocusMove.Down);      // 포커스만 a.txt

        pane.ToggleFocusedCommand.Execute(null);

        Assert.True(pane.Selection.IsSelected("Docs"));
        Assert.True(pane.Selection.IsSelected("a.txt"));

        pane.ToggleFocusedCommand.Execute(null);
        Assert.False(pane.Selection.IsSelected("a.txt"));
    }

    [Fact]
    public void ToggleFocusedCommand_WithNoFocus_DoesNothing()
    {
        var pane = CreatePane();

        pane.ToggleFocusedCommand.Execute(null);

        Assert.Equal(0, pane.Selection.Count);
    }

    // ── Enter ─────────────────────────────────────────────────────

    [Fact]
    public async Task OpenFocusedCommand_OnAFolder_NavigatesInThisPane()
    {
        var pane = await OpenAsync();
        Folder(@"C:\Temp\Docs", "inner.txt");
        pane.MoveFocusToCommand.Execute(FocusMove.End);       // 정렬상 마지막… 폴더가 먼저다

        pane.SelectItemCommand.Execute(pane.Items.First(row => row.IsDirectory));
        await pane.OpenFocusedCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Temp\Docs", pane.AddressText);
        Assert.Empty(activator.Activations);
    }

    [Fact]
    public async Task OpenFocusedCommand_OnAFile_RunsTheAssociation()
    {
        var pane = await OpenAsync();
        pane.SelectItemCommand.Execute(pane.Items.First(row => row.Name == "a.txt"));

        await pane.OpenFocusedCommand.ExecuteAsync(null);

        var opened = Assert.Single(activator.Activations);
        Assert.Equal(@"C:\Temp\a.txt", opened.DisplayPath);
    }

    [Fact]
    public async Task OpenFocusedCommand_WithNoFocus_DoesNothing()
    {
        var pane = await OpenAsync();

        await pane.OpenFocusedCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Temp", pane.AddressText);
        Assert.Empty(activator.Activations);
    }

    // ── Ctrl+A ────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAllCommand_SelectsEveryItem()
    {
        var pane = await OpenAsync();

        pane.SelectAllCommand.Execute(null);

        Assert.Equal(pane.Items.Count, pane.Selection.Count);
    }

    [Fact]
    public void SelectAllCommand_OnAnEmptyList_DoesNothing()
    {
        var pane = CreatePane();

        pane.SelectAllCommand.Execute(null);

        Assert.Equal(0, pane.Selection.Count);
    }

    // ── 마우스 — 클릭 3종 ─────────────────────────────────────────

    [Fact]
    public async Task SelectItemCommand_SelectsAloneAndFocuses()
    {
        var pane = await OpenAsync();
        pane.SelectAllCommand.Execute(null);

        pane.SelectItemCommand.Execute(pane.Items[1]);

        Assert.Equal([pane.Items[1].Name], pane.Selection.SelectedNames);
        Assert.Equal(pane.Items[1].Name, pane.FocusedName);
    }

    [Fact]
    public async Task ToggleItemCommand_AddsToTheSelectionAndFocuses()
    {
        var pane = await OpenAsync();
        pane.SelectItemCommand.Execute(pane.Items[0]);

        pane.ToggleItemCommand.Execute(pane.Items[2]);

        Assert.Equal(2, pane.Selection.Count);
        Assert.Equal(pane.Items[2].Name, pane.FocusedName);
    }

    [Fact]
    public async Task RangeSelectItemCommand_SelectsFromTheAnchorInDisplayOrder()
    {
        var pane = await OpenAsync();
        pane.SelectItemCommand.Execute(pane.Items[0]);        // 앵커

        pane.RangeSelectItemCommand.Execute(pane.Items[2]);

        Assert.Equal(3, pane.Selection.Count);
        Assert.Equal(pane.Items[2].Name, pane.FocusedName);
    }

    [Fact]
    public void ItemCommands_WithNullItem_DoNothing()
    {
        // 빈 곳 클릭이 대상 없이 들어올 수 있다.
        var pane = CreatePane();

        pane.SelectItemCommand.Execute(null);
        pane.ToggleItemCommand.Execute(null);
        pane.RangeSelectItemCommand.Execute(null);

        Assert.Equal(0, pane.Selection.Count);
        Assert.Null(pane.FocusedName);
    }

    // ── 주소줄 ────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAddressCommand_ParsesAndNavigates()
    {
        Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();

        await pane.OpenAddressCommand.ExecuteAsync(@"C:\Temp");

        Assert.Equal(@"C:\Temp", pane.AddressText);
        Assert.Single(pane.Items);
    }

    [Fact]
    public async Task OpenAddressCommand_BadAddress_ReportsWithoutThrowing()
    {
        var pane = CreatePane();

        await pane.OpenAddressCommand.ExecuteAsync("docs");

        Assert.Equal(PaneStatus.Error, pane.Status);
        Assert.Null(pane.CurrentLocation);
    }

    // ── 클릭이 페인을 활성으로 만든다 ─────────────────────────────

    [Fact]
    public async Task ItemClickCommands_RequestActivation_SoTheWorkspaceCanSwitchPanes()
    {
        // 비활성 페인의 항목을 클릭하면 선택과 활성 전환이 함께 일어나야 한다 (목업 동작).
        // 키보드 커맨드는 이미 활성 페인으로만 가므로 요청을 올리지 않는다.
        var pane = await OpenAsync();
        var requests = 0;
        pane.ActivationRequested += (_, _) => requests++;

        pane.SelectItemCommand.Execute(pane.Items[0]);
        pane.ToggleItemCommand.Execute(pane.Items[1]);
        pane.RangeSelectItemCommand.Execute(pane.Items[2]);
        pane.MoveFocusToCommand.Execute(FocusMove.Down);

        Assert.Equal(3, requests);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private async Task<PaneViewModel> OpenAsync()
    {
        Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt", @"Docs\");
        var pane = CreatePane();
        await pane.NavigateAsync(Loc(@"C:\Temp"));

        return pane;
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    /// <summary>폴더를 등록한다. 이름이 <c>\</c> 로 끝나면 디렉터리다.</summary>
    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);

        source.Folders[folder] = [.. names.Select(name =>
        {
            var isDirectory = name.EndsWith('\\');
            var bare = isDirectory ? name[..^1] : name;

            return new FileItem(
                bare,
                folder.Combine(bare),
                isDirectory ? 0 : 1024,
                DateTimeOffset.UnixEpoch,
                isDirectory ? FileItemFlags.Directory : FileItemFlags.None);
        })];

        return folder;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
