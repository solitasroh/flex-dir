using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Settings;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 탭이 창의 배선을 한 단 깊게 한다 (docs/PRD-v2.md §17 · ADR-018 · docs/ARCHITECTURE.md §4).
/// <para>
/// <b>여기서 재는 것의 본체는 배선 여섯이다</b> — 즐겨찾기 고정 · 설정의 시작 폴더 지정 ·
/// 페인 간 복사 · 이동 · 반대편 폴더 열기 · 마우스 뒤로/앞으로.
/// 전부 <c>ActivePane</c> 을 지나던 것이고 이제 "활성 페인의 <b>활성 탭</b>" 으로 간다.
/// 한 페인에 탭을 둘 두고 <b>활성이 아닌 탭이 대상이 되지 않는지</b>를 본다 — 탭이 하나뿐인
/// 페인으로 재면 전부 통과하므로 이 파일의 모든 테스트가 탭을 둘 이상 만든다.
/// </para>
/// <para>
/// 두 번째로 재는 것은 <b>cold start</b> 다. 배경 탭은 위치만 들고 있어야 하므로
/// (docs/PRD-v2.md §17 저장 포맷) 복원에서 나가는 열거는 좌·우 활성 탭 둘뿐이어야 한다 —
/// <see cref="source"/> 의 호출 횟수가 그것을 바로 낸다.
/// </para>
/// </summary>
public class WorkspaceTabsTests
{
    /// <summary>매달린 테스트를 실패로 바꾼다. 정상 동작이면 이 시간에 닿지 않는다.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private static readonly string StateDirectory = Path.Combine(Path.GetTempPath(), "flex-dir-tab-tests");

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new InMemoryViewStateStore().RememberingTwoPanes();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly FakeSettingsStore settingsStore = new();
    private readonly FakeDriveList drives = new();
    private readonly FakeFavoriteStore favorites = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 복원과 저장 ───────────────────────────────────────────────

    [Fact]
    public async Task Restore_OpensOnlyTheActiveTabOfEachPane()
    {
        // <b>cold start 가 걸리는 자리다</b> (ADR-018 · ADR-016). 탭 여섯을 복원해도 열거는
        // 둘이어야 한다 — 전작이 유일하게 성공한 축이 이것이고, 여기서 깨면 §3 의 3번에 걸린다.
        var folders = Enumerable.Range(0, 3).Select(index => Folder($@"C:\L{index}")).ToList();
        var rights = Enumerable.Range(0, 3).Select(index => Folder($@"C:\R{index}")).ToList();

        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default.WithTwoPanes(new PaneTabsState([.. folders.Select(folder => new TabState(folder))], 1), new PaneTabsState([.. rights.Select(folder => new TabState(folder))], 2)),
            CancellationToken.None);

        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(3, workspace.LeftTabs().Tabs.Count);
        Assert.Equal(3, workspace.RightTabs().Tabs.Count);
        Assert.Equal(folders[1], workspace.Left().CurrentLocation);
        Assert.Equal(rights[2], workspace.Right().CurrentLocation);

        // 순서가 곧 탭 줄의 순서다.
        Assert.Equal(folders, workspace.LeftTabs().Tabs.Select(tab => tab.CurrentLocation ?? tab.PendingLocation));

        // 이것이 이 테스트의 본체다.
        Assert.Equal([folders[1], rights[2]], source.EnumerateCalls);
    }

    [Fact]
    public async Task Restore_GivesEveryTabOfBothPanesTheHiddenItemsPolicy()
    {
        // 구조 렌즈가 잡은 것 (docs/PRD-v2.md §17): 미는 대상이 페인이 되어야 배경 탭과
        // 앞으로 만들 탭까지 정책을 받는다.
        var left = Folder(@"C:\L0");
        var background = Folder(@"C:\L1");
        settingsStore.Seed(new AppSettings { ShowHiddenItems = true });

        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default.WithTwoPanes(new PaneTabsState([new TabState(left), new TabState(background)])),
            CancellationToken.None);

        var (workspace, _, _) = CreateWithSettings();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.All(workspace.LeftTabs().Tabs, tab => Assert.True(tab.ShowHiddenItems));
        Assert.True(workspace.LeftTabs().ShowHiddenItems);
    }

    [Fact]
    public async Task Restore_InFixedStartMode_MovesOnlyTheActiveTab()
    {
        // 시작 폴더 규칙이 정하는 것은 <b>활성 탭</b>이 열 폴더다 — 배경 탭은 기억된 자기
        // 폴더를 그대로 든다 (docs/PRD-v2.md §17 세션 복원: "탭 목록 전부").
        var remembered = Folder(@"C:\L0");
        var background = Folder(@"C:\L1");
        var chosen = Folder(@"C:\Start");
        settingsStore.Seed(new AppSettings { StartMode = StartFolderMode.Fixed, StartFolder = chosen });

        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default.WithTwoPanes(new PaneTabsState([new TabState(remembered), new TabState(background)])),
            CancellationToken.None);

        var (workspace, _, _) = CreateWithSettings();
        await workspace.RestoreAsync(null, CancellationToken.None);

        Assert.Equal(chosen, workspace.Left().CurrentLocation);
        Assert.Equal(background, workspace.LeftTabs().Tabs[1].PendingLocation);
    }

    [Fact]
    public async Task Persist_SavesEveryTabOfBothPanesIncludingBackgroundOnes()
    {
        var left = Folder(@"C:\L0");
        var right = Folder(@"C:\R0");
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        await workspace.Left().NavigateAsync(left);
        await workspace.Right().NavigateAsync(right);

        // 좌 페인에 탭을 하나 더 만들고 원래 탭으로 돌아온다 — 저장 시점에 배경 탭이 있다.
        workspace.NewTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        workspace.LeftTabs().Active.CustomTitle = "일감";
        workspace.LeftTabs().Activate(workspace.LeftTabs().Tabs[0]);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        await workspace.PersistAsync();

        var state = await viewStates.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(2, state.TabsAt(0)!.Tabs.Count);
        Assert.Equal(0, state.TabsAt(0)!.ActiveIndex);
        Assert.Equal("일감", state.TabsAt(0)!.Tabs[1].Title);
        Assert.Equal(right, Assert.Single(state.TabsAt(1)!.Tabs).Folder);
    }

    // ── XAML 이 물고 있는 자리 ────────────────────────────────────

    [Fact]
    public async Task Left_IsTheActiveTabOfTheLeftPaneAndAnnouncesTheSwitch()
    {
        // View 는 Left·Right 를 물고 있다 (MainWindow.xaml 의 ContentControl 둘). 파생
        // 속성이라 값 비교로 걸러낼 수 없으므로 전환마다 알림이 나가야 한다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));

        var announced = new List<string?>();
        workspace.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        var created = workspace.LeftTabs().NewTab();
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        Assert.Same(created, workspace.Left());
        Assert.Same(created, workspace.ActiveTab);
        Assert.Contains(nameof(WorkspaceViewModel.ActiveTab), announced);
    }

    [Fact]
    public async Task Right_SwitchingATabOfTheOtherPane_AnnouncesTheOtherTab()
    {
        // 페인 간 복사의 대상이 바뀐다 — View 는 그것을 OtherTab 으로 본다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));

        var announced = new List<string?>();
        workspace.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        var created = workspace.RightTabs().NewTab();
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Same(created, workspace.Right());
        Assert.Same(created, workspace.OtherTab);
        Assert.Contains(nameof(WorkspaceViewModel.OtherTab), announced);
    }

    // ── 배선 여섯 ─────────────────────────────────────────────────

    [Fact]
    public async Task TreeNavigation_GoesToTheActiveTabOfTheActivePane()
    {
        // 배선 1 (앞방향). 트리는 페인을 모르고, 어느 페인·어느 탭인지는 워크스페이스가 정한다.
        var target = Folder(@"C:\Target");
        drives.Drives.Add(new DriveEntry(target, "Target", null));

        var (workspace, tree, _) = CreateWithTree();
        await workspace.RestoreAsync(null, CancellationToken.None);
        var (background, foreground) = await TwoTabsAsync(workspace.LeftTabs());

        tree.Roots[0].IsSelected = true;

        Assert.Equal(target, foreground.CurrentLocation);
        Assert.NotEqual(target, background.CurrentLocation);
    }

    [Fact]
    public async Task PinCurrentFolder_PinsWhatTheActiveTabIsLookingAt()
    {
        // 배선 2. 즐겨찾기는 페인을 모른다.
        var (workspace, tree, _) = CreateWithTree();
        var (background, foreground) = await TwoTabsAsync(workspace.LeftTabs());

        await workspace.PinCurrentFolderCommand.ExecuteAsync(null);

        var pinned = tree.Roots.Where(node => node.IsFavorite).Select(node => node.Location).ToList();

        Assert.Equal([foreground.CurrentLocation], pinned);
        Assert.DoesNotContain(background.CurrentLocation, pinned);
    }

    [Fact]
    public async Task UseCurrentFolderAsStart_TakesTheActiveTabsFolder()
    {
        // 배선 3. 설정은 페인을 모른다.
        var (workspace, settings, _) = CreateWithSettings();
        var (background, foreground) = await TwoTabsAsync(workspace.LeftTabs());

        workspace.UseCurrentFolderAsStartCommand.Execute(null);

        Assert.Equal(foreground.CurrentLocation!.DisplayPath, settings.StartFolderText);
        Assert.NotEqual(background.CurrentLocation!.DisplayPath, settings.StartFolderText);
    }

    [Fact]
    public async Task CopyToOtherPane_SendsFromTheActiveTabToTheOtherPanesActiveTab()
    {
        // 배선 4 — 양쪽 끝이 모두 한 단 깊어진다. 2분할의 존재 이유가 이 왕복이다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (_, leftActive) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        var (rightBackground, rightActive) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");

        leftActive.Selection.SelectSingle("a.txt");
        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        var (_, destination) = Assert.Single(operations.Copies);
        Assert.Equal(rightActive.CurrentLocation, destination);
        Assert.NotEqual(rightBackground.CurrentLocation, destination);
    }

    [Fact]
    public async Task MoveToOtherPane_SendsFromTheActiveTabToTheOtherPanesActiveTab()
    {
        // 배선 5.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (_, leftActive) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        var (rightBackground, rightActive) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");

        leftActive.Selection.SelectSingle("a.txt");
        await workspace.MoveToOtherPaneCommand.ExecuteAsync(null);

        var (_, destination) = Assert.Single(operations.Moves);
        Assert.Equal(rightActive.CurrentLocation, destination);
        Assert.NotEqual(rightBackground.CurrentLocation, destination);
    }

    [Fact]
    public async Task OpenOtherPaneLocation_MovesTheActiveTabToTheOtherPanesActiveTabsFolder()
    {
        // 배선 6.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (leftBackground, leftActive) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        var (_, rightActive) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");

        await workspace.OpenOtherPaneLocationCommand.ExecuteAsync(null);

        Assert.Equal(rightActive.CurrentLocation, leftActive.CurrentLocation);
        Assert.NotEqual(rightActive.CurrentLocation, leftBackground.CurrentLocation);
    }

    [Fact]
    public async Task GoBackAt_ATabOfTheRightPane_MovesThatPanesActiveTabAndActivatesThePane()
    {
        // 배선 7 (docs/PRD-v2.md §15). 마우스에는 <b>자리가 있다</b> — 누른 자리의 페인이
        // 움직이고 그 페인이 활성이 된다. 대상은 그 페인의 활성 탭이다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (rightBackground, rightActive) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");
        var second = Folder(@"C:\R-second");
        await rightActive.NavigateAsync(second);
        var origin = rightActive.CurrentLocation;

        await workspace.GoBackAtAsync(rightActive);

        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.NotEqual(origin, rightActive.CurrentLocation);
        Assert.Equal(second, (await GoForwardAsync(workspace, rightActive)).CurrentLocation);

        // 배경 탭은 자기 히스토리를 그대로 들고 있다.
        Assert.NotNull(rightBackground.CurrentLocation);
    }

    [Fact]
    public async Task GoBackAt_ABackgroundTabOfAPane_StillTargetsThatPanesActiveTab()
    {
        // 화면에 보이는 것은 활성 탭뿐이지만, 판정은 "어느 페인의 탭인가" 로 한다 — 그러면
        // 배경 탭 참조가 와도 옳은 페인이 움직인다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (rightBackground, rightActive) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");
        await rightActive.NavigateAsync(Folder(@"C:\R-second"));
        var backgroundFolder = rightBackground.CurrentLocation;

        await workspace.GoBackAtAsync(rightBackground);

        Assert.Same(workspace.RightTabs(), workspace.ActivePane);

        // 움직인 것은 활성 탭이다. 배경 탭은 제자리다.
        Assert.Equal(backgroundFolder, rightBackground.CurrentLocation);

        // 이 탭의 히스토리는 R0(복제) → R1 → R-second 이므로 뒤로는 R1 이다.
        Assert.Equal(@"C:\R1", rightActive.CurrentLocation!.DisplayPath);
    }

    // ── 키보드 탭 조작 ────────────────────────────────────────────

    [Fact]
    public async Task NewTabCommand_AddsToTheActivePaneOnly()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        workspace.ActivateCommand.Execute(workspace.RightTabs());

        workspace.NewTabCommand.Execute(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal(2, workspace.RightTabs().Tabs.Count);
        Assert.Single(workspace.LeftTabs().Tabs);
    }

    [Fact]
    public async Task NextTabAndPreviousTab_CycleInsideTheActivePane()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        var (first, second) = await TwoTabsAsync(workspace.LeftTabs());

        workspace.NextTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        Assert.Same(first, workspace.Left());

        workspace.PreviousTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        Assert.Same(second, workspace.Left());
    }

    [Fact]
    public async Task CloseTabCommand_OnTheLastTab_DoesNothing()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));

        await workspace.CloseTabCommand.ExecuteAsync(null);

        Assert.Single(workspace.LeftTabs().Tabs);

        // 되살릴 것도 없다 — 닫히지 않았으므로 스택이 비어 있어야 한다.
        workspace.ReopenClosedTabCommand.Execute(null);
        Assert.Single(workspace.LeftTabs().Tabs);
    }

    [Fact]
    public async Task CloseTabCommand_OnAPinnedTab_DoesNothing()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await TwoTabsAsync(workspace.LeftTabs());
        workspace.Left().IsPinned = true;

        await workspace.CloseTabCommand.ExecuteAsync(null);

        Assert.Equal(2, workspace.LeftTabs().Tabs.Count);
    }

    // ── 반대편 페인으로 보내기 (docs/PRD-v2.md §17) ────────────────

    [Fact]
    public async Task SendTabToOtherPane_MovesTheSameInstanceAndKeepsTheActivePane()
    {
        // 살아 있는 인스턴스의 소유권이 페인을 건너간다 — 히스토리·선택이 따라오는 것이
        // "새로 만들고 상태를 복사" 가 아니라는 증거다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (leftKept, leftMoving) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));

        leftMoving.Selection.SelectSingle("a.txt");
        var folder = leftMoving.CurrentLocation;

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        // 같은 인스턴스가 반대편에 서 있다.
        Assert.DoesNotContain(leftMoving, workspace.LeftTabs().Tabs);
        Assert.Contains(leftMoving, workspace.RightTabs().Tabs);
        Assert.Same(leftMoving, workspace.RightTabs().Active);
        Assert.Equal(folder, leftMoving.CurrentLocation);
        Assert.Equal(["a.txt"], leftMoving.Selection.SelectedNames);

        // 활성 페인은 따라가지 않는다 (사용자 결정 2026-08-11) — 보내는 것은 정리 동작이다.
        Assert.Same(workspace.LeftTabs(), workspace.ActivePane);
        Assert.Same(leftKept, workspace.ActiveTab);
    }

    [Fact]
    public async Task SendTabToOtherPane_StandsRightAfterTheDestinationsActiveTab()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (_, moving) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        var (rightFirst, rightSecond) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");

        // 도착 페인의 활성 탭을 첫 번째로 되돌린다 — 착지가 "맨 뒤" 가 아님을 보려면 활성
        // 탭이 맨 뒤가 아니어야 한다.
        workspace.RightTabs().Activate(rightFirst);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal([rightFirst, moving, rightSecond], workspace.RightTabs().Tabs);
    }

    [Fact]
    public async Task SendTabToOtherPane_WithATab_SendsThatOneNotTheActiveOne()
    {
        // 컨텍스트 메뉴가 싣는 것은 우클릭한 탭이다 — 우클릭은 탭을 활성으로 만들지 않으므로
        // (브라우저·탐색기와 같다) 활성 탭으로만 받으면 엉뚱한 것이 건너간다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (background, active) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));

        Assert.Same(active, workspace.LeftTabs().Active);

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(background);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Contains(background, workspace.RightTabs().Tabs);
        Assert.Same(active, Assert.Single(workspace.LeftTabs().Tabs));
    }

    [Fact]
    public async Task SendTabToOtherPane_WithATabFromTheRightPane_SendsItLeft()
    {
        // 드래그는 어느 페인에서든 시작한다. 어느 쪽의 탭인지는 목록이 안다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));
        var (_, moving) = await TwoTabsAsync(workspace.RightTabs(), prefix: "R");

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(moving);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        Assert.Contains(moving, workspace.LeftTabs().Tabs);
        Assert.DoesNotContain(moving, workspace.RightTabs().Tabs);
    }

    [Fact]
    public async Task SendTabToOtherPane_TheLastTab_DoesNothing()
    {
        // 보내면 그 페인이 탭 0개가 된다 — "페인은 항상 둘" 전제가 깨진다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        var only = workspace.Left();
        await only.NavigateAsync(Folder(@"C:\L0"));
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(null);

        Assert.Same(only, Assert.Single(workspace.LeftTabs().Tabs));
        Assert.Single(workspace.RightTabs().Tabs);
    }

    [Fact]
    public async Task SendTabToOtherPane_APinnedTab_StillMoves()
    {
        // 고정은 "닫히지 않는다" 이지 "움직이지 않는다" 가 아니다. 고정 여부는 인스턴스에
        // 붙어 있어 건너간 뒤에도 그대로다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (_, moving) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));
        moving.IsPinned = true;

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Contains(moving, workspace.RightTabs().Tabs);
        Assert.True(moving.IsPinned);
    }

    [Fact]
    public async Task SendTabToOtherPane_ThenPersist_RecordsTheTabOnItsNewSide()
    {
        // 소유권이 건너갔다는 것은 저장 포맷에서도 보여야 한다 — 안 그러면 다음 실행에
        // 원래 페인으로 돌아간다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        var (_, moving) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));
        var folder = moving.CurrentLocation;

        await workspace.SendTabToOtherPaneCommand.ExecuteAsync(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);
        await workspace.PersistAsync();

        var state = await viewStates.LoadGlobalAsync(CancellationToken.None);

        Assert.DoesNotContain(folder, state.TabsAt(0)!.Tabs.Select(tab => tab.Folder));
        Assert.Contains(folder, state.TabsAt(1)!.Tabs.Select(tab => tab.Folder));
    }

    // ── 닫은 탭 되살리기 (Ctrl+Shift+T) ───────────────────────────

    [Fact]
    public async Task ReopenClosedTab_ReturnsToItsOriginalPaneAtItsOriginalIndexAndBecomesActive()
    {
        // <b>창 전체에 스택 하나다</b> (docs/PRD-v2.md §17) — 닫은 것이 어느 페인이었는지
        // 기억하지 않아도 된다. 되살린 탭이 활성이 되고 그 페인도 활성이 된다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        // 우 페인에 탭 셋을 만들고 가운데를 닫는다.
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));
        workspace.ActivateCommand.Execute(workspace.RightTabs());
        workspace.NewTabCommand.Execute(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);
        var closing = workspace.Right();
        var closingFolder = Folder(@"C:\R-middle");
        await closing.NavigateAsync(closingFolder);
        workspace.NewTabCommand.Execute(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal(3, workspace.RightTabs().Tabs.Count);
        Assert.Equal(1, workspace.RightTabs().Tabs.IndexOf(closing));

        workspace.RightTabs().Activate(closing);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);
        await workspace.CloseTabCommand.ExecuteAsync(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal(2, workspace.RightTabs().Tabs.Count);

        // 닫은 직후 반대편으로 옮겨가서 누른다 — 페인별 스택이면 여기서 돌아오지 않는다.
        workspace.ActivateCommand.Execute(workspace.LeftTabs());

        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.RightTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal(3, workspace.RightTabs().Tabs.Count);
        Assert.Equal(closingFolder, workspace.RightTabs().Tabs[1].CurrentLocation);
        Assert.Same(workspace.RightTabs().Tabs[1], workspace.RightTabs().Active);
        Assert.Same(workspace.RightTabs(), workspace.ActivePane);
        Assert.Same(workspace.RightTabs().Tabs[1], workspace.ActiveTab);
    }

    [Fact]
    public async Task ReopenClosedTab_KeepsThePinAndTheUserTitle()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await TwoTabsAsync(workspace.LeftTabs());

        workspace.Left().CustomTitle = "일감";
        await workspace.CloseTabCommand.ExecuteAsync(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal("일감", workspace.Left().Title);
    }

    [Fact]
    public async Task ReopenClosedTab_TakesTheMostRecentFirst()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));

        var first = await AddTabAsync(workspace, Folder(@"C:\First"));
        var second = await AddTabAsync(workspace, Folder(@"C:\Second"));

        await CloseAsync(workspace, second);
        await CloseAsync(workspace, first);

        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        Assert.Equal(@"C:\First", workspace.Left().CurrentLocation!.DisplayPath);

        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        Assert.Equal(@"C:\Second", workspace.Left().CurrentLocation!.DisplayPath);
    }

    [Fact]
    public async Task ReopenClosedTab_BeyondTenClosedTabs_DropsTheOldest()
    {
        // 깊이 10 이다 (docs/PRD-v2.md §17). 넘치면 버리는 것은 <b>가장 오래된 것</b>이다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));

        for (var index = 0; index < 11; index++)
        {
            await CloseAsync(workspace, await AddTabAsync(workspace, Folder($@"C:\T{index}")));
        }

        for (var index = 10; index >= 1; index--)
        {
            workspace.ReopenClosedTabCommand.Execute(null);
            await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

            Assert.Equal($@"C:\T{index}", workspace.Left().CurrentLocation!.DisplayPath);
        }

        // 열한 번째로 오래된 것(T0)은 버려졌다 — 더 눌러도 아무 일도 없다.
        var before = workspace.LeftTabs().Tabs.Count;
        workspace.ReopenClosedTabCommand.Execute(null);

        Assert.Equal(before, workspace.LeftTabs().Tabs.Count);
    }

    [Fact]
    public async Task ReopenClosedTab_AfterCloseOthers_BringsThemAllBackToTheirPlaces()
    {
        // 닫는 길이 다섯이라 되살리기 스택은 <b>한 신호</b>로 채워져야 한다. 컨텍스트 메뉴로
        // 닫은 것이 스택에 안 들어가면 그 탭만 조용히 되살아나지 않는다.
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);
        await workspace.Left().NavigateAsync(Folder(@"C:\L0"));

        var kept = workspace.Left();
        await AddTabAsync(workspace, Folder(@"C:\First"));
        await AddTabAsync(workspace, Folder(@"C:\Second"));

        await workspace.LeftTabs().CloseOthersAsync(kept).WaitAsync(Limit);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        Assert.Single(workspace.LeftTabs().Tabs);

        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);
        workspace.ReopenClosedTabCommand.Execute(null);
        await workspace.LeftTabs().SwitchWork.WaitAsync(Limit);

        Assert.Equal(
            [@"C:\L0", @"C:\First", @"C:\Second"],
            workspace.LeftTabs().Tabs.Select(tab => tab.CurrentLocation!.DisplayPath));
    }

    [Fact]
    public async Task ReopenClosedTab_WithNothingClosed_DoesNothing()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreAsync(null, CancellationToken.None);

        workspace.ReopenClosedTabCommand.Execute(null);

        Assert.Single(workspace.LeftTabs().Tabs);
        Assert.Single(workspace.RightTabs().Tabs);
    }

    // ── 숨김 정책 변경 ────────────────────────────────────────────

    [Fact]
    public async Task HiddenItemsChanged_ReachesEveryTabButOnlyRereadsTheActiveOnes()
    {
        // 정책은 모든 탭에, 다시 읽기는 활성 탭 둘에. 전부 읽으면 클릭 한 번에 저장소 호출이
        // 탭 수만큼 나간다 — 배경 탭은 활성이 될 때 도는 새로 고침이 새 정책으로 읽는다.
        var (workspace, settings, _) = CreateWithSettings();
        await workspace.RestoreAsync(null, CancellationToken.None);
        var (leftBackground, _) = await TwoTabsAsync(workspace.LeftTabs(), prefix: "L");
        await workspace.Right().NavigateAsync(Folder(@"C:\R0"));

        var before = source.EnumerateCalls.Count;
        settings.ShowHiddenItems = true;
        await workspace.HiddenItemsWork;

        Assert.All(workspace.LeftTabs().Tabs, tab => Assert.True(tab.ShowHiddenItems));
        Assert.True(leftBackground.ShowHiddenItems);
        Assert.Equal(before + 2, source.EnumerateCalls.Count);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private WorkspaceViewModel CreateWorkspace() => new WorkspaceViewModel(CreatePane, viewStates).Split2();

    private (WorkspaceViewModel Workspace, FolderTreeViewModel Tree, object? Unused) CreateWithTree()
    {
        var tree = new FolderTreeViewModel(drives, new FakeNetworkPlaceList(), favorites, source, dispatcher);

        return (new WorkspaceViewModel(CreatePane, viewStates, update: null, tree).Split2(), tree, null);
    }

    private (WorkspaceViewModel Workspace, SettingsViewModel Settings, object? Unused) CreateWithSettings()
    {
        var settings = new SettingsViewModel(settingsStore, dispatcher, "0.4.3", StateDirectory, new FakeSystemThemeSource());

        return (new WorkspaceViewModel(CreatePane, viewStates, null, null, settings).Split2(), settings, null);
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>
    /// 페인에 탭 둘을 세우고 (배경, 활성) 을 낸다. 각자 <b>다른</b> 폴더를 본다 — 같은 폴더면
    /// "활성 탭이 대상인가" 를 가릴 수 없다.
    /// </summary>
    private async Task<(PaneViewModel Background, PaneViewModel Foreground)> TwoTabsAsync(
        PaneTabsViewModel tabs,
        string prefix = "L")
    {
        var background = tabs.Active;
        await background.NavigateAsync(Folder($@"C:\{prefix}0"));

        var foreground = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await foreground.NavigateAsync(Folder($@"C:\{prefix}1"));

        return (background, foreground);
    }

    /// <summary>활성 페인에 탭을 하나 더 만들어 그 폴더를 열고 돌려준다.</summary>
    private async Task<PaneViewModel> AddTabAsync(WorkspaceViewModel workspace, LocationId folder)
    {
        workspace.NewTabCommand.Execute(null);
        await workspace.ActivePane.SwitchWork.WaitAsync(Limit);

        var tab = workspace.ActiveTab;
        await tab.NavigateAsync(folder);

        return tab;
    }

    private static async Task CloseAsync(WorkspaceViewModel workspace, PaneViewModel tab)
    {
        workspace.ActivePane.Activate(tab);
        await workspace.ActivePane.SwitchWork.WaitAsync(Limit);
        await workspace.CloseTabCommand.ExecuteAsync(null);
        await workspace.ActivePane.SwitchWork.WaitAsync(Limit);
    }

    private static async Task<PaneViewModel> GoForwardAsync(WorkspaceViewModel workspace, PaneViewModel tab)
    {
        await workspace.GoForwardAtAsync(tab);

        return tab;
    }

    /// <summary>폴더를 등록하고 항목 하나를 넣는다 — 선택이 필요한 배선 테스트가 쓴다.</summary>
    private LocationId Folder(string path)
    {
        var folder = Loc(path);

        source.Folders[folder] = [new FileItem("a.txt", folder.Combine("a.txt"), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None)];

        return folder;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }
}
