using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 페인 하나의 탭 목록과 활성 탭 (docs/PRD-v2.md §17 · ADR-018).
/// <para>
/// 여기서 재는 것의 중심은 <b>세 가지</b>다. (1) 탭이 살아 있는가 — 전환이 즉시여야 탭이
/// 왕복 비용을 줄인다. (2) 그러나 <b>감시는 활성 탭만</b> 드는가 — docs/PRD-v2.md §13 의
/// 감시 오버플로 폭주가 탭 수만큼 곱해지는 것을 막는 자리다. (3) 배경 탭이 열거를 미리
/// 하지 않는가 — cold start 는 전작이 유일하게 성공한 축이다 (ADR-016).
/// </para>
/// <para>
/// 포트는 탭들이 나눠 쓴다 — 실제 조립도 그렇다 (<c>AppComposition</c>). 그래서
/// <see cref="source"/> 의 호출 횟수가 "몇 개의 탭이 저장소를 두드렸는가" 를 바로 낸다.
/// </para>
/// </summary>
public class PaneTabsViewModelTests
{
    /// <summary>매달린 테스트를 실패로 바꾼다. 정상 동작이면 이 시간에 닿지 않는다.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

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
    public void New_HasExactlyOneActiveTab()
    {
        // 탭 줄은 탭이 1개여도 항상 보인다 (docs/PRD-v2.md §17) — 그래서 "탭 0개" 는
        // 어느 시점에도 존재하지 않는다.
        var tabs = CreateTabs();

        Assert.Same(Assert.Single(tabs.Tabs), tabs.Active);
        Assert.Equal(0, tabs.ActiveIndex);
    }

    [Fact]
    public void New_DoesNotEnumerateAnything()
    {
        // 조립은 화면 없이 서야 하고 (AppComposition) 폴더를 여는 것은 복원의 일이다.
        _ = CreateTabs();

        Assert.Empty(source.EnumerateCalls);
    }

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PaneTabsViewModel(null!));
    }

    // ── 새 탭 (Ctrl+T) ────────────────────────────────────────────

    [Fact]
    public async Task NewTab_StandsRightAfterTheActiveTabAndBecomesActive()
    {
        // 사용자 결정 2026-08-11 (docs/PRD-v2.md §17): 제목이 같아도 방금 생긴 것이 어느
        // 쪽인지 보이고, 원본으로 돌아가는 길이 바로 왼쪽이다.
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        var third = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        // 세 번째는 두 번째(활성) 바로 오른쪽이다 — 맨 뒤가 아니다.
        Assert.Equal([first, second, third], tabs.Tabs);
        Assert.Same(third, tabs.Active);
    }

    [Fact]
    public async Task NewTab_InTheMiddle_GoesRightAfterTheActiveOneNotAtTheEnd()
    {
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        // 맨 앞으로 돌아가서 연다.
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);
        var inserted = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal([first, inserted, second], tabs.Tabs);
    }

    [Fact]
    public async Task NewTab_ClonesTheFolderTheUserIsLookingAt()
    {
        // "여기서 잠깐 다른 데 다녀오겠다" 가 탭을 여는 순간의 뜻이므로, 복제면 돌아올
        // 자리가 보존된다 (docs/PRD-v2.md §17).
        var folder = Folder(@"C:\A", "a.txt", "b.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(folder, tabs.Active.CurrentLocation);
        Assert.Equal(2, tabs.Active.Items.Count);
    }

    [Fact]
    public async Task NewTab_InheritsTheHiddenItemsPolicy()
    {
        // 구조 렌즈가 잡은 것 (docs/PRD-v2.md §17): 워크스페이스가 페인 둘에만 밀고 있어서
        // 나중에 만든 탭이 기본값으로 뜨던 자리다.
        await using var tabs = CreateTabs();
        tabs.ShowHiddenItems = true;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.True(tabs.Active.ShowHiddenItems);
    }

    [Fact]
    public async Task NewTab_IsNotPinnedAndHasNoUserTitle()
    {
        await using var tabs = CreateTabs();
        tabs.Active.IsPinned = true;
        tabs.Active.CustomTitle = "일감";
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.False(tabs.Active.IsPinned);
        Assert.Null(tabs.Active.CustomTitle);
    }

    // ── 컬럼 폭은 페인이 소유한다 (docs/PRD-v2.md §17 구조 렌즈) ────

    [Fact]
    public async Task Columns_AreSharedByEveryTabOfThePane()
    {
        // 탭 하나가 인스턴스 하나라 소유자를 올리지 않으면 탭 전환마다 컬럼이 튄다.
        // §11 이 "페인마다 따로" 로 정한 결론은 그대로 산다 — 페인 <b>안에서는</b> 공유다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var first = tabs.Active;

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        // 새 탭에서 경계를 끈다.
        tabs.Active.NameColumnWidth = 420;

        Assert.Equal(420, first.NameColumnWidth);
        Assert.Equal(420, tabs.Columns.Name);
    }

    [Fact]
    public async Task Columns_SurviveATabSwitch()
    {
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var first = tabs.Active;
        first.NameColumnWidth = 420;

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(420, second.NameColumnWidth);
        Assert.Equal(420, tabs.Active.NameColumnWidth);
    }

    [Fact]
    public async Task Columns_SetOnThePane_ReachEveryTab()
    {
        // 복원 경로다 — 저장된 폭을 페인에 밀면 모든 탭이 그것을 본다.
        await using var tabs = CreateTabs();
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        tabs.Columns = new PaneColumns(400, 100, 130, 150);

        Assert.All(tabs.Tabs, tab => Assert.Equal(400, tab.NameColumnWidth));
    }

    // ── 닫기 (Ctrl+W) ─────────────────────────────────────────────

    [Fact]
    public async Task Close_TheLastTab_IsRefused()
    {
        // 한쪽 페인의 마지막 탭이 창을 닫으면 반대편 페인까지 함께 사라진다. 페인은 항상
        // 둘이라는 v1 전제를 탭이 깨지 않는다 (docs/PRD-v2.md §17).
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        Assert.Null(await tabs.CloseAsync(tabs.Active).WaitAsync(Limit));
        Assert.Single(tabs.Tabs);
    }

    [Fact]
    public async Task Close_APinnedTab_IsRefused()
    {
        // 고정 탭은 닫기 버튼이 없고 Ctrl+W 도 무시된다 — 먼저 고정을 풀어야 닫힌다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Active.IsPinned = true;

        Assert.Null(await tabs.CloseAsync(tabs.Active).WaitAsync(Limit));
        Assert.Equal(2, tabs.Tabs.Count);
    }

    [Fact]
    public async Task Close_ReportsWhereTheTabWasAndWhatItHeld()
    {
        // 되살리기가 원래 자리로 돌아가려면 이 기록이 필요하다 (docs/PRD-v2.md §17).
        var folder = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Active.CustomTitle = "일감";

        var closed = await tabs.CloseAsync(tabs.Active).WaitAsync(Limit);

        Assert.NotNull(closed);
        Assert.Equal(1, closed.Index);
        Assert.Equal(folder, closed.State.Folder);
        Assert.Equal("일감", closed.State.Title);
    }

    [Fact]
    public async Task Close_TheActiveTab_ActivatesTheOneToItsLeft()
    {
        // 사용자 결정 2026-08-11. Ctrl+T 가 오른쪽에 세우므로, 잠깐 다녀온 탭을 닫으면
        // 출발한 탭으로 돌아온다 — 새 탭 규칙과 짝이다.
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.CloseAsync(tabs.Active).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(first, tabs.Active);
    }

    [Fact]
    public async Task Close_TheFirstTab_ActivatesTheOneToItsRight()
    {
        // 왼쪽이 없으면 오른쪽이다. 활성 탭은 항상 정확히 하나여야 한다.
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.CloseAsync(first).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(second, tabs.Active);
    }

    [Fact]
    public async Task Close_ABackgroundTab_LeavesTheActiveOneAlone()
    {
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.CloseAsync(first).WaitAsync(Limit);

        Assert.Same(second, tabs.Active);
        Assert.Same(second, Assert.Single(tabs.Tabs));
    }

    [Fact]
    public async Task Close_EndsTheClosedTabsWatch()
    {
        // 닫은 탭은 인스턴스를 접는다 (사용자 결정 2026-08-11) — 되살리기 스택은 기록만
        // 든다. 접지 않으면 감시·썸네일 스케줄러·열거 세션이 배경에 남는다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.CloseAsync(tabs.Active).WaitAsync(Limit);

        // 새 탭이 활성이 되며 첫 탭의 감시가 풀렸고(1), 닫으며 자기 감시가 풀린다(2).
        Assert.Equal(2, watcher.CancellationsObserved);
    }

    // ── 감시는 활성 탭만 든다 (ADR-018) ────────────────────────────

    [Fact]
    public async Task Deactivating_DropsTheWatch()
    {
        // 이 저장소가 실물에서 값을 치른 자리다 (docs/PRD-v2.md §13). 탭 8개가 각자
        // 감시하면 그 사건이 8배로 돌아온다.
        var folder = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);
        var first = tabs.Active;

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(1, watcher.CancellationsObserved);

        // 목록은 그대로다 — 놓는 것은 감시 하나뿐이다.
        Assert.Single(first.Items);
        Assert.Equal(folder, first.CurrentLocation);
    }

    [Fact]
    public async Task Reactivating_RefreshesOnceThenWatchesAgain()
    {
        // 배경 탭은 외부 변경을 즉시 모른다 — 활성화 시점의 새로 고침이 그것을 메운다.
        var folder = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);
        var first = tabs.Active;

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        // 배경에 있는 동안 밖에서 늘었다.
        source.Folders[folder] = [.. source.Folders[folder], Entry(folder, "b.txt")];

        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(2, first.Items.Count);

        // 감시가 다시 걸렸는가 — 이후의 변경은 알림만으로 들어와야 한다.
        var watching = await WatchingAsync(folder);

        source.Folders[folder] = [.. source.Folders[folder], Entry(folder, "c.txt")];
        watching.Push(new FolderChange(FolderChangeKind.Added, "c.txt"));

        await WaitForAsync(() => first.Items.Count == 3, "다시 건 감시가 변경을 나른다");
    }

    [Fact]
    public async Task Reactivating_RefreshesExactlyOnce()
    {
        // 한 번이다. 활성화마다 두 번 읽으면 NAS·WSL 에서 탭 전환이 폴더 이동보다 비싸진다.
        var folder = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);
        var first = tabs.Active;

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        var before = source.EnumerateCalls.Count;
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(before + 1, source.EnumerateCalls.Count);
    }

    [Fact]
    public async Task Reactivating_KeepsTheSelection()
    {
        // 갱신 중에도 선택은 유지한다 (CLAUDE.md §4) — 탭을 다녀온 것이 선택을 풀면
        // 페인 간 복사의 출발점이 사라진다.
        var folder = Folder(@"C:\A", "a.txt", "b.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, folder);
        var first = tabs.Active;
        first.Selection.SelectSingle("b.txt");

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(["b.txt"], first.Selection.SelectedNames);
    }

    [Fact]
    public async Task SwitchingBackAndForthQuickly_LeavesNoTabWatchingInTheBackground()
    {
        // 구조 렌즈가 잡은 것 (docs/PRD-v2.md §17): 전환이 겹치면 이전 탭의 해제와 새 탭의
        // 구독이 경합한다. 앞선 작업을 취소하되 <b>놓기는 취소하지 않는다</b> — 버려진
        // 전환이 감시를 든 탭을 배경에 남기면 §13 폭주가 화면 밖에서 돈다.
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, a);
        var first = tabs.Active;

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await second.NavigateAsync(b);

        // 이 세대에만 밀어넣기 위해 잡아 둔다 — 인스턴스 단위 Push 는 가장 최근 세대로 간다.
        var watchingB = await WatchingAsync(b);

        // 겹쳐 던진다. 사이에서 기다리지 않는다.
        tabs.Activate(first);
        tabs.Activate(second);
        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(first, tabs.Active);

        // 활성이 아닌 탭에 밀어넣은 변경은 목록에 닿지 않아야 한다.
        source.Folders[b] = [.. source.Folders[b], Entry(b, "b2.txt")];
        watchingB.Push(new FolderChange(FolderChangeKind.Added, "b2.txt"));

        // 일어나지 않는 일은 알림으로 기다릴 수 없다. 이 테스트만 짧게 재운다.
        await Task.Delay(50);

        Assert.Single(second.Items);
    }

    // ── 순환 (Ctrl+Tab) ───────────────────────────────────────────

    [Fact]
    public async Task Next_WrapsAroundToTheFirstTab()
    {
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        tabs.Next();
        await tabs.SwitchWork.WaitAsync(Limit);
        Assert.Same(first, tabs.Active);

        tabs.Next();
        await tabs.SwitchWork.WaitAsync(Limit);
        Assert.Same(second, tabs.Active);
    }

    [Fact]
    public async Task Previous_WrapsAroundToTheLastTab()
    {
        await using var tabs = CreateTabs();
        var first = tabs.Active;
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);
        tabs.Previous();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(second, tabs.Active);
    }

    [Fact]
    public async Task Next_WithASingleTab_DoesNothing()
    {
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var only = tabs.Active;
        var before = source.EnumerateCalls.Count;

        tabs.Next();
        await tabs.SwitchWork.WaitAsync(Limit);

        // 새로 고침조차 돌지 않는다 — 전환이 없었기 때문이다.
        Assert.Same(only, tabs.Active);
        Assert.Equal(before, source.EnumerateCalls.Count);
    }

    // ── 복원 (docs/PRD-v2.md §17 저장 포맷) ────────────────────────

    [Fact]
    public async Task Restore_OpensOnlyTheActiveTab()
    {
        // 시작 비용은 지금과 같다 (ADR-018) — 배경 탭은 위치만 들고 있다가 처음 활성화될
        // 때 연다. cold start 는 전작이 유일하게 성공한 축이다.
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        var c = Folder(@"C:\C", "c.txt");
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(
            new PaneTabsState([new TabState(a), new TabState(b), new TabState(c)], ActiveIndex: 1),
            b).WaitAsync(Limit);

        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Same(tabs.Tabs[1], tabs.Active);
        Assert.Equal([b], source.EnumerateCalls);
    }

    [Fact]
    public async Task Restore_GivesBackgroundTabsTheirTitlesWithoutOpeningThem()
    {
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\Work", "b.txt");
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(new PaneTabsState([new TabState(a), new TabState(b)]), a).WaitAsync(Limit);

        Assert.Equal("Work", tabs.Tabs[1].Title);
        Assert.Null(tabs.Tabs[1].CurrentLocation);
    }

    [Fact]
    public async Task Restore_KeepsPinsAndUserTitles()
    {
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(
            new PaneTabsState([new TabState(a, IsPinned: true), new TabState(b, Title: "일감")]),
            a).WaitAsync(Limit);

        Assert.True(tabs.Tabs[0].IsPinned);
        Assert.Equal("일감", tabs.Tabs[1].Title);
    }

    [Fact]
    public async Task Restore_OpensTheActiveTabAtTheFolderItWasGiven()
    {
        // 시작 폴더 규칙은 워크스페이스가 지난다 (AppSettings.ResolveStartFolder). 여기로는
        // 그 결과가 온다 — 기억된 폴더와 다를 수 있다.
        var remembered = Folder(@"C:\A", "a.txt");
        var start = Folder(@"C:\Start", "s.txt");
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(PaneTabsState.Single(remembered), start).WaitAsync(Limit);

        Assert.Equal(start, tabs.Active.CurrentLocation);
        Assert.Equal([start], source.EnumerateCalls);
    }

    [Fact]
    public async Task Restore_WithoutAnyMemory_KeepsOneTab()
    {
        var start = Folder(@"C:\Start", "s.txt");
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(null, start).WaitAsync(Limit);

        Assert.Single(tabs.Tabs);
        Assert.Equal(start, tabs.Active.CurrentLocation);
    }

    [Fact]
    public async Task Restore_WithoutAnyFolderAtAll_LeavesThePaneEmpty()
    {
        // 첫 실행이고 폴백조차 없다. 빈 페인은 오류가 아니다.
        await using var tabs = CreateTabs();

        await tabs.RestoreAsync(null, null).WaitAsync(Limit);

        Assert.Single(tabs.Tabs);
        Assert.Empty(source.EnumerateCalls);
    }

    [Fact]
    public async Task Restore_GivesEveryRestoredTabTheHiddenItemsPolicy()
    {
        // 정책은 폴더를 열기 전에 서 있어야 한다 — 나중에 밀면 시작할 때 한 번은 옛
        // 정책으로 그려진다.
        var a = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        tabs.ShowHiddenItems = true;

        await tabs.RestoreAsync(new PaneTabsState([new TabState(a), new TabState(a)]), a).WaitAsync(Limit);

        Assert.All(tabs.Tabs, tab => Assert.True(tab.ShowHiddenItems));
    }

    // ── 저장 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Capture_RecordsEveryTabInOrderWithTheActiveOne()
    {
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await tabs.RestoreAsync(new PaneTabsState([new TabState(a), new TabState(b)], 1), b).WaitAsync(Limit);

        var captured = tabs.Capture();

        Assert.NotNull(captured);
        Assert.Equal([a, b], captured.Tabs.Select(tab => tab.Folder));
        Assert.Equal(1, captured.ActiveIndex);
    }

    [Fact]
    public async Task Capture_RecordsABackgroundTabByTheFolderItNeverOpened()
    {
        // 한 번도 활성이 된 적 없는 탭도 다음 실행에 살아 있어야 한다 — 안 그러면 앱을
        // 두 번 켜는 것만으로 배경 탭이 사라진다.
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await tabs.RestoreAsync(new PaneTabsState([new TabState(a), new TabState(b)]), a).WaitAsync(Limit);

        var captured = tabs.Capture();

        Assert.Equal([a, b], captured!.Tabs.Select(tab => tab.Folder));
    }

    [Fact]
    public async Task Capture_KeepsPinsAndUserTitles()
    {
        var a = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await tabs.RestoreAsync(PaneTabsState.Single(a), a).WaitAsync(Limit);
        tabs.Active.IsPinned = true;
        tabs.Active.CustomTitle = "일감";

        var tab = Assert.Single(tabs.Capture()!.Tabs);

        Assert.True(tab.IsPinned);
        Assert.Equal("일감", tab.Title);
    }

    [Fact]
    public void Capture_WithNothingOpened_RemembersNothing()
    {
        // 창을 띄우고 아무 곳도 열지 않은 채 끝났다. "탭 0개" 를 저장하면 다음 실행이
        // 그것을 기억으로 받아 시작 폴더 규칙을 지나지 못한다.
        Assert.Null(CreateTabs().Capture());
    }

    [Fact]
    public async Task Capture_SkipsTabsWithoutAFolderAndKeepsPointingAtTheActiveOne()
    {
        // 새 탭을 열었는데 폴더를 열지 못한 상태로 종료할 수 있다. 그 탭이 빠지면서
        // 활성 탭 번호가 당겨져야 한다 — 그대로 두면 다음 실행이 엉뚱한 탭으로 뜬다.
        var a = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        var empty = tabs.Active;
        var opened = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await opened.NavigateAsync(a);

        Assert.Null(empty.CurrentLocation);

        var captured = tabs.Capture();

        Assert.Equal(a, Assert.Single(captured!.Tabs).Folder);
        Assert.Equal(0, captured.ActiveIndex);
    }

    // ── 되살리기가 쓰는 자리 ──────────────────────────────────────

    [Fact]
    public async Task Insert_PutsTheTabBackWhereItWasAndActivatesIt()
    {
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await tabs.RestoreAsync(new PaneTabsState([new TabState(a), new TabState(a)]), a).WaitAsync(Limit);

        var revived = tabs.Insert(1, new TabState(b, IsPinned: true, Title: "일감"));
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Same(revived, tabs.Tabs[1]);
        Assert.Same(revived, tabs.Active);
        Assert.Equal(b, revived.CurrentLocation);
        Assert.True(revived.IsPinned);
        Assert.Equal("일감", revived.Title);
    }

    [Fact]
    public async Task Insert_WithAnIndexPastTheEnd_PutsTheTabLast()
    {
        // 되살리는 사이에 탭을 여럿 닫았을 수 있다. 던지면 그 조작이 통째로 사라진다.
        var a = Folder(@"C:\A", "a.txt");
        await using var tabs = CreateTabs();
        await tabs.RestoreAsync(PaneTabsState.Single(a), a).WaitAsync(Limit);

        var revived = tabs.Insert(9, new TabState(a));
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(revived, tabs.Tabs[^1]);
    }

    // ── 밖으로 내는 신호 ──────────────────────────────────────────

    [Fact]
    public async Task ActivationRequested_FromAnyTab_ReachesThePane()
    {
        // 비활성 페인의 항목 클릭은 선택과 활성 전환이 한 동작이다. 페인이 둘에서 탭
        // 여럿으로 늘어도 워크스페이스가 무는 곳은 여전히 둘이어야 한다.
        await using var tabs = CreateTabs();
        var asked = 0;
        tabs.ActivationRequested += (_, _) => asked++;

        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        tabs.Active.SelectItemCommand.Execute(tabs.Active.Items[0]);

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task ActiveLocationChanged_FiresOnBothNavigationAndTabSwitch()
    {
        // 트리가 따라가는 근거다 (docs/PRD-v2.md §10-3). 탭 전환도 "활성 페인이 보는 폴더가
        // 바뀌었다" 이므로 같은 자리를 지나야 한다.
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, a);

        var first = tabs.Active;
        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await second.NavigateAsync(b);

        var changed = 0;
        tabs.ActiveLocationChanged += (_, _) => changed++;

        tabs.Activate(first);
        await tabs.SwitchWork.WaitAsync(Limit);
        Assert.True(changed >= 1, "탭 전환이 트리를 옮긴다");

        await first.NavigateAsync(b);
        Assert.True(changed >= 2, "활성 탭의 이동이 트리를 옮긴다");
    }

    [Fact]
    public async Task ActiveLocationChanged_DoesNotFireForBackgroundTabs()
    {
        // 배경 탭이 트리를 끌고 다니면 트리가 어느 쪽을 가리키는지 알 수 없고, 그 탐색은
        // 전부 저장소 호출이다.
        var a = Folder(@"C:\A", "a.txt");
        var b = Folder(@"C:\B", "b.txt");
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, a);
        var background = tabs.Active;

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        var changed = 0;
        tabs.ActiveLocationChanged += (_, _) => changed++;

        await background.NavigateAsync(b);

        Assert.Equal(0, changed);
    }

    // ── 정리 ──────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_FoldsTheActiveTabsWatchAndDoesNotHang()
    {
        // 여기서 상한에 걸리면 어느 탭의 감시나 열거의 종료 신호를 놓친 것이다 — 실패하지
        // 않고 매달리는 쪽이 더 나쁘다 (PaneWatcherTests 와 같은 판단).
        var a = Folder(@"C:\A", "a.txt");
        var tabs = CreateTabs();
        await OpenAsync(tabs, a);
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.DisposeAsync().AsTask().WaitAsync(Limit);

        // 첫 탭은 비활성이 되며 이미 놓았고(1), 활성 탭은 여기서 놓인다(2).
        Assert.Equal(2, watcher.CancellationsObserved);
    }

    [Fact]
    public async Task DisposeAsync_FoldsBackgroundTabsToo()
    {
        // <b>배경 탭이 접히는지를 재는 유일한 자리다.</b> 감시로는 잴 수 없다 — 배경 탭은
        // 이미 놓았기 때문이다. 썸네일 스케줄러는 탭마다 살아 있으므로 (ADR-018) 진행 중인
        // 그림 요청을 열어 두면 그 탭이 접혔는지가 취소 관측으로 나온다.
        //
        // 상주 프로세스라 창만 닫히고 프로세스는 남는다 (ADR-003) — 접지 않은 스케줄러는
        // BGRA 버퍼와 진행 중 요청을 그대로 들고 배경에 남는다.
        var a = Folder(@"C:\A", "a.png");
        var tabs = CreateTabs();
        await OpenAsync(tabs, a);
        var background = tabs.Active;

        // 배경으로 보내기 전에 요청을 열어 둔다. 이 관문은 취소로만 풀린다.
        thumbnails.ThumbnailGate = new TaskCompletionSource().Task;
        background.SetVisibleRange([.. background.Items]);

        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        await tabs.DisposeAsync().AsTask().WaitAsync(Limit);

        Assert.True(thumbnails.CancellationsObserved >= 1, "배경 탭의 진행 중 요청이 접힌다");
    }

    [Fact]
    public async Task DisposeAsync_Twice_IsHarmless()
    {
        // 상주 프로세스의 종료 경로가 두 번 지날 수 있다 (AppComposition.DisposeAsync 도
        // 같은 이유로 막아 두었다).
        var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        await tabs.DisposeAsync().AsTask().WaitAsync(Limit);
        await tabs.DisposeAsync().AsTask().WaitAsync(Limit);

        Assert.Equal(1, watcher.CancellationsObserved);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneTabsViewModel CreateTabs() => new(CreatePane);

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    /// <summary>활성 탭으로 폴더를 연다. 복원을 지나지 않는 짧은 길이다.</summary>
    private static Task OpenAsync(PaneTabsViewModel tabs, LocationId folder)
        => tabs.Active.NavigateAsync(folder);

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);
        source.Folders[folder] = [.. names.Select(name => Entry(folder, name))];

        return folder;
    }

    private static FileItem Entry(LocationId folder, string name)
        => new(name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }

    /// <summary>
    /// 그 폴더의 감시가 <b>실제로 걸릴 때까지</b> 기다리고 그 세대를 낸다.
    /// <para>
    /// <b>이 대기 없이 변경을 밀어넣으면 간헐적으로 잃는다.</b> 감시를 거는 것은
    /// <c>Task.Run</c> 으로 떼어져 있어 (<c>PaneViewModel.StartWatching</c> — 감시를 거는
    /// 것 자체가 shell 호출이라 UI 스레드 밖이어야 한다) 새로 고침이나 폴더 열기를 기다린
    /// 시점에 <c>WatchAsync</c> 는 아직 안 불렸을 수 있다. 그때 <c>watcher.Push</c> 는
    /// <b>직전(취소된) 세대</b>의 채널로 들어가고 읽는 사람이 없다 — 부하가 걸린 실행에서만
    /// 드러났다.
    /// </para>
    /// </summary>
    private async Task<FakeFolderWatcher.WatchStream> WatchingAsync(LocationId folder)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (watcher.Current is { } stream && stream.Folder.Equals(folder))
            {
                return stream;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"감시가 걸리기를 기다리다 상한을 넘겼다: {folder.DisplayPath}");

        throw new InvalidOperationException("닿지 않는다 — Assert.Fail 이 던진다.");
    }

    /// <summary>
    /// 감시 스트림은 배경에서 도므로 테스트가 완료 시점을 직접 잡을 수 없다. 상한은 대기
    /// 시간이 아니라 매달린 테스트를 실패로 바꾸는 장치다 (PaneWatcherTests 와 같은 수).
    /// </summary>
    private static async Task WaitForAsync(Func<bool> reached, string expectation)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (reached())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"기다리다 상한을 넘겼다: {expectation}");
    }
}
