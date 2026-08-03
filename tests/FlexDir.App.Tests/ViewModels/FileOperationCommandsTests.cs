using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 복사·이동·삭제·이름변경·새 폴더, 그리고 페인 간 이동 (docs/PRD.md §2).
/// <para>
/// 여기서 재는 것의 절반은 <b>일어나지 않아야 하는 일</b>이다 — 조작 후에 목록을 직접
/// 고치지 않고(감시가 갱신한다, CLAUDE.md §4), 폴더 진입이 <c>IItemActivator</c> 를 타지
/// 않고(그러면 shell 이 새 탐색기 창을 띄운다), 선택이 비면 아무 일도 하지 않는다.
/// </para>
/// <para>
/// 커맨드는 <c>[RelayCommand]</c> 가 만든 것을 부른다. <c>CanExecute</c> 로 막는 것과
/// 실행됐을 때 안전한 것은 <b>서로 다른 요구</b>다 — 경합이 있으므로 둘 다 본다.
/// </para>
/// </summary>
public class FileOperationCommandsTests
{
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 선택이 비었을 때 ──────────────────────────────────────────

    [Fact]
    public async Task WithNothingSelected_EveryOperationCommandDoesNothing()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        // CanExecute 로 막힌다.
        Assert.False(pane.CopySelectionCommand.CanExecute(null));
        Assert.False(pane.CutSelectionCommand.CanExecute(null));
        Assert.False(pane.DeleteSelectionCommand.CanExecute(null));
        Assert.False(pane.BeginRenameCommand.CanExecute(null));

        // 그래도 실행됐을 때 안전해야 한다 — CanExecute 와 실행 사이에 경합이 있다.
        pane.CopySelectionCommand.Execute(null);
        pane.CutSelectionCommand.Execute(null);
        pane.BeginRenameCommand.Execute(null);
        await pane.DeleteSelectionCommand.ExecuteAsync(null);

        Assert.Empty(clipboard.CopySets);
        Assert.Empty(clipboard.CutSets);
        Assert.Empty(operations.Deletes);
        Assert.Null(pane.RenamingName);
        Assert.Equal(PaneStatus.Idle, pane.Status);
    }

    [Fact]
    public async Task WithNothingSelected_ThePaneToPaneCommandsDoNothing()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(pics);

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);
        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
    }

    // ── 클립보드 ──────────────────────────────────────────────────

    [Fact]
    public async Task CopySelection_PutsTheSelectedItemsOnTheClipboard()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.Selection.Toggle("c.txt");

        Assert.True(pane.CopySelectionCommand.CanExecute(null));
        pane.CopySelectionCommand.Execute(null);

        Assert.Equal(
            [folder.Combine("a.txt"), folder.Combine("c.txt")],
            Assert.Single(clipboard.CopySets));
        Assert.Empty(clipboard.CutSets);
    }

    [Fact]
    public async Task CutSelection_ThenPaste_Moves()
    {
        // 잘라내기 뒤의 붙여넣기는 이동이다 — 복사면 원본이 남는다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var backup = Folder(@"C:\Temp\Backup");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);
        pane.Selection.SelectSingle("a.txt");

        pane.CutSelectionCommand.Execute(null);
        await pane.NavigateAsync(backup);
        await pane.PasteCommand.ExecuteAsync(null);

        var move = Assert.Single(operations.Moves);
        Assert.Equal([docs.Combine("a.txt")], move.Sources);

        // 대상은 붙여넣는 시점의 현재 폴더다.
        Assert.Equal(backup, move.Destination);
        Assert.Empty(operations.Copies);
    }

    [Fact]
    public async Task CopySelection_ThenPaste_Copies()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var backup = Folder(@"C:\Temp\Backup");
        var pane = CreatePane();
        await pane.NavigateAsync(docs);
        pane.Selection.SelectSingle("a.txt");

        pane.CopySelectionCommand.Execute(null);
        await pane.NavigateAsync(backup);
        await pane.PasteCommand.ExecuteAsync(null);

        var copy = Assert.Single(operations.Copies);
        Assert.Equal([docs.Combine("a.txt")], copy.Sources);
        Assert.Equal(backup, copy.Destination);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task Paste_WithAnEmptyClipboard_DoesNothing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.PasteCommand.ExecuteAsync(null);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
        Assert.Equal(1, clipboard.PasteQueries);
    }

    [Fact]
    public async Task Paste_BeforeAnyNavigation_DoesNothing()
    {
        // 붙여넣을 대상 폴더가 없다.
        var pane = CreatePane();
        clipboard.SetCopy([Loc(@"C:\Temp\a.txt")]);

        await pane.PasteCommand.ExecuteAsync(null);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
    }

    // ── 삭제 ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSelection_SendsTheSelectedItems()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");

        Assert.True(pane.DeleteSelectionCommand.CanExecute(null));
        await pane.DeleteSelectionCommand.ExecuteAsync(null);

        Assert.Equal([folder.Combine("b.txt")], Assert.Single(operations.Deletes));
    }

    [Fact]
    public async Task DeleteSelection_WhenItFails_ReportsTheReasonWithoutThrowing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");

        var locked = folder.Combine("a.txt");
        operations.Failure = new LocationAccessException(LocationErrorKind.Sharing, locked);

        await pane.DeleteSelectionCommand.ExecuteAsync(null);

        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.Sharing, locked), pane.StatusText);

        // 실패했으므로 목록은 더더욱 그대로다.
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 목록은 감시가 갱신한다 ────────────────────────────────────

    [Fact]
    public async Task SuccessfulOperations_DoNotTouchTheListThemselves()
    {
        // CLAUDE.md §4 — 낙관적 갱신은 실패 시 유령 항목을 남긴다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        var rows = pane.Items.ToList();

        pane.CopySelectionCommand.Execute(null);
        await pane.PasteCommand.ExecuteAsync(null);
        await pane.DeleteSelectionCommand.ExecuteAsync(null);
        await pane.CreateFolderCommand.ExecuteAsync(null);

        Assert.Single(operations.Copies);
        Assert.Single(operations.Deletes);
        Assert.Single(operations.CreatedFolders);

        // 같은 인스턴스가 같은 순서로 남아 있다.
        Assert.Equal(rows, pane.Items);
        Assert.True(pane.Selection.IsSelected("a.txt"));
    }

    // ── 활성화 ────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_OnAFolder_EntersItWithoutTheItemActivator()
    {
        // 폴더 진입을 IItemActivator 에 맡기면 shell 이 새 탐색기 창을 띄운다.
        var root = Folder(@"C:\Temp", @"Docs\");
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(root);

        await pane.ActivateCommand.ExecuteAsync(pane.Items[0]);

        Assert.Equal(docs, pane.CurrentLocation);
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
        Assert.Empty(activator.Activations);

        // 히스토리에 남는다 — 뒤로가 원래 폴더로 돌아간다.
        Assert.True(pane.CanGoBack);
    }

    [Fact]
    public async Task Activate_OnAFile_UsesTheItemActivator()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ActivateCommand.ExecuteAsync(pane.Items[0]);

        Assert.Equal(folder.Combine("a.txt"), Assert.Single(activator.Activations));

        // 파일을 여는 것은 탐색이 아니다.
        Assert.Equal(folder, pane.CurrentLocation);
        Assert.False(pane.CanGoBack);
    }

    [Fact]
    public async Task Activate_OnACloudPlaceholder_StillUsesTheItemActivator()
    {
        // 다운로드를 트리거하는 것은 사용자가 의도한 행위다. 피해야 하는 것은 썸네일뿐이다.
        var folder = Loc(@"C:\Temp");
        source.Folders[folder] =
        [
            new FileItem(
                "cloud.txt",
                folder.Combine("cloud.txt"),
                1024,
                DateTimeOffset.UnixEpoch,
                FileItemFlags.CloudPlaceholder),
        ];
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        var row = Assert.Single(pane.Items);
        Assert.True(row.Item.IsContentAccessRisky);

        await pane.ActivateCommand.ExecuteAsync(row);

        Assert.Equal(folder.Combine("cloud.txt"), Assert.Single(activator.Activations));
    }

    [Fact]
    public async Task Activate_WithNoItem_DoesNothing()
    {
        // 빈 곳을 더블클릭하면 선택된 줄이 없다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.ActivateCommand.ExecuteAsync(null);

        Assert.Empty(activator.Activations);
        Assert.Single(source.EnumerateCalls);
    }

    [Fact]
    public async Task Activate_WhenTheProgramCannotBeLaunched_ReportsTheReason()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        var missing = folder.Combine("a.txt");
        activator.Failure = new LocationAccessException(LocationErrorKind.NotFound, missing);

        await pane.ActivateCommand.ExecuteAsync(pane.Items[0]);

        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, missing), pane.StatusText);
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 이름변경 ──────────────────────────────────────────────────

    [Fact]
    public async Task BeginRename_WithTwoSelected_DoesNotStart()
    {
        // 무엇의 이름을 바꾸는지 알 수 없다. 일괄 이름변경은 v1 범위 밖이다 (docs/PRD.md §3).
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.Selection.Toggle("b.txt");

        Assert.False(pane.BeginRenameCommand.CanExecute(null));
        pane.BeginRenameCommand.Execute(null);

        Assert.Null(pane.RenamingName);
    }

    [Fact]
    public async Task BeginRename_WithOneSelected_StartsEditingThatName()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");

        Assert.True(pane.BeginRenameCommand.CanExecute(null));
        pane.BeginRenameCommand.Execute(null);

        Assert.Equal("b.txt", pane.RenamingName);
    }

    [Fact]
    public async Task CancelRename_StopsEditing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.BeginRenameCommand.Execute(null);

        pane.CancelRenameCommand.Execute(null);

        Assert.Null(pane.RenamingName);
        Assert.Empty(operations.Renames);
    }

    [Fact]
    public async Task CommitRename_RenamesTheItemAndStopsEditing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.BeginRenameCommand.Execute(null);

        await pane.CommitRenameCommand.ExecuteAsync("b.txt");

        Assert.Equal((folder.Combine("a.txt"), "b.txt"), Assert.Single(operations.Renames));
        Assert.Null(pane.RenamingName);

        // 목록은 감시가 갱신한다 (CLAUDE.md §4).
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a.txt")]
    public async Task CommitRename_WithNothingToChange_JustCancelsTheEdit(string? newName)
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.BeginRenameCommand.Execute(null);

        await pane.CommitRenameCommand.ExecuteAsync(newName);

        Assert.Null(pane.RenamingName);
        Assert.Empty(operations.Renames);
        Assert.Equal(PaneStatus.Idle, pane.Status);
    }

    [Fact]
    public async Task CommitRename_WhenNotEditing_DoesNothing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.CommitRenameCommand.ExecuteAsync("b.txt");

        Assert.Empty(operations.Renames);
    }

    [Fact]
    public async Task CommitRename_WhenItFails_ShowsTheReasonAndLeavesTheListAlone()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.BeginRenameCommand.Execute(null);

        var item = folder.Combine("a.txt");
        operations.Failure = new LocationAccessException(LocationErrorKind.AccessDenied, item);

        await pane.CommitRenameCommand.ExecuteAsync("c.txt");

        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, item), pane.StatusText);

        // 목록을 직접 고치지 않았으므로 실패해도 유령 항목이 없다.
        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
        Assert.Null(pane.RenamingName);
    }

    // ── 새 폴더 ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateFolder_UsesTheDefaultNameAndStartsRenamingIt()
    {
        // 탐색기와 같은 흐름이다 — 만든 뒤 이름 편집이 열린다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.CreateFolderCommand.ExecuteAsync(null);

        Assert.Equal((folder, "새 폴더"), Assert.Single(operations.CreatedFolders));
        Assert.Equal("새 폴더", pane.RenamingName);

        // 목록에 넣지 않는다 — 감시가 갱신한다.
        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task CreateFolder_FollowsTheNameTheImplementationActuallyMade()
    {
        // 이름이 겹치면 구현체가 유일한 이름을 만든다. 요청한 이름으로 편집을 열면
        // 존재하지 않는 항목의 이름을 바꾸게 된다.
        var folder = Folder(@"C:\Temp", "새 폴더\\");
        operations.CreatedFolderName = "새 폴더 (2)";
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await pane.CreateFolderCommand.ExecuteAsync(null);

        Assert.Equal("새 폴더 (2)", pane.RenamingName);

        // 그리고 그 이름으로 이름변경이 이어진다.
        await pane.CommitRenameCommand.ExecuteAsync("보고서");

        Assert.Equal((folder.Combine("새 폴더 (2)"), "보고서"), Assert.Single(operations.Renames));
    }

    [Fact]
    public async Task CreateFolder_WhenItFails_ReportsTheReasonAndDoesNotStartEditing()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        operations.Failure = new LocationAccessException(LocationErrorKind.AccessDenied, folder);

        await pane.CreateFolderCommand.ExecuteAsync(null);

        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, folder), pane.StatusText);
        Assert.Null(pane.RenamingName);
    }

    [Fact]
    public async Task CreateFolder_BeforeAnyNavigation_DoesNothing()
    {
        var pane = CreatePane();

        await pane.CreateFolderCommand.ExecuteAsync(null);

        Assert.Empty(operations.CreatedFolders);
        Assert.Null(pane.RenamingName);
    }

    // ── 페인 간 복사·이동 ─────────────────────────────────────────

    [Fact]
    public async Task CopyToOtherPane_TargetsTheInactivePaneFolder()
    {
        // 2분할의 존재 이유가 이 왕복이다 (docs/PRD.md §2).
        var docs = Folder(@"C:\Temp\Docs", "a.txt", "b.txt");
        var backup = Folder(@"C:\Temp\Backup");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(backup);
        workspace.Left.Selection.SelectSingle("a.txt");

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        var copy = Assert.Single(operations.Copies);
        Assert.Equal([docs.Combine("a.txt")], copy.Sources);
        Assert.Equal(backup, copy.Destination);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task MoveToOtherPane_TargetsTheInactivePaneFolder()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var backup = Folder(@"C:\Temp\Backup");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(backup);
        workspace.Left.Selection.SelectSingle("a.txt");

        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        var move = Assert.Single(operations.Moves);
        Assert.Equal([docs.Combine("a.txt")], move.Sources);
        Assert.Equal(backup, move.Destination);
        Assert.Empty(operations.Copies);
    }

    [Fact]
    public async Task PaneToPaneCommands_FollowTheActiveSide()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var backup = Folder(@"C:\Temp\Backup", "z.txt");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        await workspace.Right.NavigateAsync(backup);
        workspace.Right.Selection.SelectSingle("z.txt");
        workspace.ActivateCommand.Execute(PaneSide.Right);

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        var copy = Assert.Single(operations.Copies);
        Assert.Equal([backup.Combine("z.txt")], copy.Sources);
        Assert.Equal(docs, copy.Destination);
    }

    [Fact]
    public async Task PaneToPaneCommands_WithNothingOpenOnTheOtherSide_DoNothing()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        workspace.Left.Selection.SelectSingle("a.txt");

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);
        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        Assert.Empty(operations.Copies);
        Assert.Empty(operations.Moves);
        Assert.Null(workspace.Right.CurrentLocation);
    }

    [Fact]
    public async Task MoveToOtherPane_WithTheSameFolderOnBothSides_DoesNothing()
    {
        // 제자리 이동은 아무 일도 아니다. 복사는 shell 이 사본을 만든다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(folder);
        await workspace.Right.NavigateAsync(folder);
        workspace.Left.Selection.SelectSingle("a.txt");

        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        Assert.Empty(operations.Moves);

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Single(operations.Copies);
    }

    // ── 진행 중에도 반대편 페인이 움직인다 ───────────────────────

    [Fact]
    public async Task AnOperationInFlight_DoesNotBlockTheOtherPane()
    {
        // UI 를 잠그는 "작업 중" 플래그를 두면 2분할의 이점이 사라진다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        var workspace = CreateWorkspace();
        await workspace.Left.NavigateAsync(docs);
        workspace.Left.Selection.SelectSingle("a.txt");

        var released = new TaskCompletionSource();
        operations.Gate = released.Task;
        var pending = workspace.Left.DeleteSelectionCommand.ExecuteAsync(null);

        Assert.False(pending.IsCompleted);

        // 삭제가 매달려 있는 동안 반대편 페인은 정상적으로 폴더를 연다.
        await workspace.Right.NavigateAsync(pics);

        Assert.Equal(pics, workspace.Right.CurrentLocation);
        Assert.Equal(["p.jpg"], workspace.Right.Items.Select(row => row.Name));
        Assert.Equal(PaneStatus.Idle, workspace.Right.Status);

        released.SetResult();
        await pending;

        Assert.Single(operations.Deletes);
    }

    // ── 알림 ──────────────────────────────────────────────────────

    [Fact]
    public async Task SelectionChange_RefreshesTheOperationCommandsCanExecute()
    {
        // 툴바 버튼이 켜지는 근거다 — CanExecute 값만 맞고 알림이 없으면 WPF 가 모른다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        var notified = 0;
        pane.DeleteSelectionCommand.CanExecuteChanged += (_, _) => notified++;

        pane.Selection.SelectSingle("a.txt");

        Assert.True(notified > 0);
        Assert.True(pane.DeleteSelectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task RenamingName_RaisesPropertyChanged()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");

        var changed = new List<string?>();
        pane.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        pane.BeginRenameCommand.Execute(null);
        pane.CancelRenameCommand.Execute(null);

        Assert.Equal(
            [nameof(PaneViewModel.RenamingName), nameof(PaneViewModel.RenamingName)],
            changed);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private WorkspaceViewModel CreateWorkspace() => new(CreatePane(), CreatePane(), viewStates);

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

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
