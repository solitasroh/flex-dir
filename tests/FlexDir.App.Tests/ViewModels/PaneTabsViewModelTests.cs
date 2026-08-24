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

    // ── 그릴 순서 (docs/DESIGN.md §1-1 고정 탭) ────────────────────
    // 고정 탭은 줄 맨 왼쪽에 모인다. **목록 순서는 건드리지 않는다** (사용자 결정
    // 2026-08-11) — 그래야 고정을 풀 때 원래 자리로 돌아가고 저장 포맷이 흔들리지 않는다.
    // 그 결정이 표현되는 자리가 여기 하나뿐이라, XAML 이 정렬을 알지 않아도 된다.

    [Fact]
    public async Task StripTabs_WithoutAnyPin_IsTheListItself()
    {
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(tabs.Tabs, tabs.StripTabs);
    }

    [Fact]
    public async Task StripTabs_GathersPinnedTabsFirstWithoutMovingThemInTheList()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        second.IsPinned = true;

        // 그리는 순서만 바뀐다.
        Assert.Equal([second, first, third], tabs.StripTabs);

        // 목록은 제자리다 — 저장 포맷이 이 순서다.
        Assert.Equal([first, second, third], tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task StripTabs_KeepsTheListOrderInsideEachGroup()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        third.IsPinned = true;
        first.IsPinned = true;

        // 고정끼리도 목록 순서다 — 고정한 순서가 아니다. 뒤엣것을 먼저 고정했다고 화면이
        // 뒤집히면 어느 탭이 어디로 갔는지 알 수 없다.
        Assert.Equal([first, third, second], tabs.StripTabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task StripTabs_AnnouncesAPinToggle()
    {
        // 탭 줄이 다시 그릴 근거다. 파생 속성이라 값 비교로 걸러낼 수 없다.
        var (tabs, first, second, _) = await ThreeTabsAsync();
        var announced = 0;
        tabs.PropertyChanged += (_, args)
            => announced += args.PropertyName == nameof(PaneTabsViewModel.StripTabs) ? 1 : 0;

        second.IsPinned = true;

        Assert.True(announced > 0);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task TogglePin_FlipsThePinWithoutMovingTheTab()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        tabs.TogglePin(second);
        Assert.True(second.IsPinned);
        Assert.Equal([first, second, third], tabs.Tabs);

        tabs.TogglePin(second);
        Assert.False(second.IsPinned);

        // 고정을 풀면 그리는 순서도 원래대로다.
        Assert.Equal([first, second, third], tabs.StripTabs);

        await tabs.DisposeAsync();
    }

    // ── 떼기와 받기는 한 쌍이다 (docs/PRD-v2.md §17 페인 간 이동) ──
    // 부르는 곳이 둘로 늘었다 — 컨텍스트 메뉴('반대편 페인으로 보내기')와 탭 드래그.
    // 순서를 각자 쓰면 한쪽이 <c>DetachAsync</c> 의 null(마지막 탭)을 빠뜨리는 순간 그 탭이
    // 어느 페인에도 없는 채로 사라진다.

    [Fact]
    public async Task SendAsync_HandsTheLivingInstanceToTheOtherPane()
    {
        var (tabs, first, _, third) = await ThreeTabsAsync();
        await using var other = CreateTabs();

        await tabs.SendAsync(third, other);
        await other.SwitchWork.WaitAsync(Limit);

        Assert.DoesNotContain(third, tabs.Tabs);
        Assert.Contains(third, other.Tabs);
        Assert.Same(third, other.Active);

        // 보낸 쪽의 활성은 왼쪽 이웃으로 간다 — 닫기와 같은 규칙이다.
        Assert.Same(tabs.Tabs[^1], tabs.Active);
        Assert.Contains(first, tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_TheLastTab_MovesNothing()
    {
        // 보내면 이 페인이 탭 0개가 되어 "페인은 항상 둘" 이 깨진다. 떼기가 거부하므로
        // 받는 쪽도 아무 일이 없어야 한다 — 여기가 갈리면 탭이 양쪽에 하나씩 생긴다.
        await using var tabs = CreateTabs();
        await using var other = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var only = tabs.Active;

        await tabs.SendAsync(only, other);

        Assert.Same(only, Assert.Single(tabs.Tabs));
        Assert.Single(other.Tabs);
    }

    // ── 순서 바꾸기 (탭 드래그 · docs/PRD-v2.md §17) ───────────────
    // 자리는 <b>StripTabs 기준</b>이다 — 사용자가 끌어다 놓는 자리가 그것이다. 목록 기준으로
    // 받으면 고정 탭이 있는 페인에서 손이 놓은 곳과 다른 자리에 선다.

    [Fact]
    public async Task MoveTab_PutsTheTabAtThatPlaceInTheStrip()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        tabs.MoveTab(first, 2);

        Assert.Equal([second, third, first], tabs.StripTabs);
        Assert.Equal([second, third, first], tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task MoveTab_Backwards_PutsTheTabAtThatPlaceToo()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        tabs.MoveTab(third, 0);

        Assert.Equal([third, first, second], tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task MoveTab_AnUnpinnedTab_CannotJumpAheadOfThePinnedOnes()
    {
        // 고정 탭은 줄 맨 왼쪽에 모인다 (docs/DESIGN.md §1-1). 그 앞으로 끌어다 놓아도
        // 그려지는 자리는 고정 탭 뒤이므로, 목록만 바뀌고 화면은 그대로면 "놓은 자리에
        // 안 간다" 로 보인다. 자기 무리 안으로 자른다 — 결정이 아니라 그 규칙의 적용이다.
        var (tabs, first, second, third) = await ThreeTabsAsync();

        second.IsPinned = true;
        Assert.Equal([second, first, third], tabs.StripTabs);

        tabs.MoveTab(third, 0);

        // 고정 탭 바로 뒤 = 비고정 무리의 맨 앞이다.
        Assert.Equal([second, third, first], tabs.StripTabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task MoveTab_APinnedTab_StaysAmongThePinnedOnes()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        first.IsPinned = true;
        second.IsPinned = true;
        Assert.Equal([first, second, third], tabs.StripTabs);

        tabs.MoveTab(first, 2);

        // 고정끼리의 순서만 바뀌고 비고정 앞을 지키다.
        Assert.Equal([second, first, third], tabs.StripTabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task MoveTab_KeepsTheActiveTabAndAnnouncesItsNewIndex()
    {
        // 활성은 인스턴스로 따라간다. 번호만 바뀌므로 저장 포맷이 그것을 다시 읽어야 한다.
        var (tabs, first, _, third) = await ThreeTabsAsync();
        var announced = 0;
        tabs.PropertyChanged += (_, args)
            => announced += args.PropertyName == nameof(PaneTabsViewModel.ActiveIndex) ? 1 : 0;

        Assert.Same(third, tabs.Active);

        tabs.MoveTab(third, 0);

        Assert.Same(third, tabs.Active);
        Assert.Equal(0, tabs.ActiveIndex);
        Assert.True(announced > 0);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task MoveTab_ATabFromAnotherPane_DoesNothing()
    {
        // 드롭 대상이 반대편 페인이면 그것은 소유권 이동(DetachAsync/Receive)이고 이 길이
        // 아니다. 던지지 않는다 — 드래그가 두 경로를 지나는 동안 순서가 뒤집힐 수 있다.
        var (tabs, _, _, _) = await ThreeTabsAsync();
        await using var other = CreateTabs();

        tabs.MoveTab(other.Active, 0);

        Assert.Equal(3, tabs.Tabs.Count);

        await tabs.DisposeAsync();
    }

    // ── 복제 (컨텍스트 메뉴) ───────────────────────────────────────

    [Fact]
    public async Task Duplicate_StandsRightAfterTheTabItCopiedAndOpensTheSameFolder()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        var copy = tabs.Duplicate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        // 활성 탭 오른쪽이 아니라 <b>복제한 탭</b> 오른쪽이다 — 우클릭한 탭이 기준이다.
        Assert.Equal([first, copy, second, third], tabs.Tabs);
        Assert.Same(copy, tabs.Active);
        Assert.Equal(first.CurrentLocation, copy.CurrentLocation);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task Duplicate_DoesNotCarryThePinOrTheUserTitle()
    {
        // Ctrl+T 와 같은 규칙이다 (docs/PRD-v2.md §17: 복제하는 것은 <b>폴더</b>다).
        // 제목까지 따라오면 같은 이름 둘이 서고 어느 쪽이 원본인지 알 수 없다.
        var (tabs, first, _, _) = await ThreeTabsAsync();
        first.IsPinned = true;
        first.CustomTitle = "일감";

        var copy = tabs.Duplicate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.False(copy.IsPinned);
        Assert.Null(copy.CustomTitle);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task Duplicate_ABackgroundTab_CopiesThatTabsFolderNotTheActiveOnes()
    {
        var (tabs, first, _, third) = await ThreeTabsAsync();

        // third 가 활성이다 (ThreeTabsAsync). 배경에 있는 first 를 복제한다.
        Assert.Same(third, tabs.Active);

        var copy = tabs.Duplicate(first);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(first.CurrentLocation, copy.CurrentLocation);
        Assert.NotEqual(third.CurrentLocation, copy.CurrentLocation);

        await tabs.DisposeAsync();
    }

    // ── 여러 개 닫기 (컨텍스트 메뉴) ───────────────────────────────

    [Fact]
    public async Task CloseOthers_KeepsTheChosenTabAndActivatesIt()
    {
        var (tabs, first, _, _) = await ThreeTabsAsync();

        await tabs.CloseOthersAsync(first).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(first, Assert.Single(tabs.Tabs));
        Assert.Same(first, tabs.Active);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task CloseOthers_KeepsPinnedTabs()
    {
        // 고정 탭은 닫히지 않는다 — 먼저 고정을 풀어야 한다 (docs/PRD-v2.md §17).
        var (tabs, first, second, _) = await ThreeTabsAsync();
        second.IsPinned = true;

        await tabs.CloseOthersAsync(first).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal([first, second], tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task CloseToTheRight_ClosesEverythingAfterItOnTheStrip()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        await tabs.CloseToTheRightAsync(second).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal([first, second], tabs.Tabs);
        Assert.False(third.IsPinned);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task CloseToTheRight_JudgesByTheStripOrderNotTheList()
    {
        // 여기서만 둘이 갈린다: 고정 탭을 우클릭할 때다. 사용자가 보는 것이 그릴 순서이므로
        // 그것을 기준으로 판정해야 한다 — 목록 기준이면 고정 탭 왼쪽에 그려진 탭이
        // "오른쪽" 으로 잡혀 함께 닫힌다.
        var (tabs, first, second, third) = await ThreeTabsAsync();

        second.IsPinned = true;

        // 그릴 순서: [second☉] [first] [third]
        Assert.Equal([second, first, third], tabs.StripTabs);

        await tabs.CloseToTheRightAsync(second).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        // second 오른쪽에 그려진 것은 first·third 둘 다다.
        Assert.Same(second, Assert.Single(tabs.Tabs));

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task CloseToTheRight_OnTheRightmostTab_ClosesNothing()
    {
        var (tabs, first, second, third) = await ThreeTabsAsync();

        await tabs.CloseToTheRightAsync(third).WaitAsync(Limit);

        Assert.Equal([first, second, third], tabs.Tabs);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task ClosingTabs_ReportsEachOneOnce()
    {
        // 되살리기 스택이 이 신호로 채워진다 — 어느 경로로 닫혔든 (Ctrl+W · 가운데 버튼 ·
        // 메뉴 셋) 한 자리로 모여야 스택이 새지 않는다.
        var (tabs, first, _, _) = await ThreeTabsAsync();
        var reported = new List<ClosedTab>();
        tabs.TabClosed += (_, closed) => reported.Add(closed);

        await tabs.CloseOthersAsync(first).WaitAsync(Limit);

        Assert.Equal(2, reported.Count);

        // 오른쪽부터 닫는다 — 되살리기가 최근 것부터 꺼내면 원래 자리가 그대로 복원된다.
        Assert.Equal([2, 1], reported.Select(closed => closed.Index));

        await tabs.DisposeAsync();
    }

    // ── 페인 간 이동 (docs/PRD-v2.md §17 페인 간 이동) ─────────────
    // 살아 있는 인스턴스의 소유권이 페인을 건너간다. 감시 구독·썸네일 스케줄러·열거 세션·
    // 히스토리·선택이 <b>전부 따라온다</b> — 그것이 "새로 만들고 상태를 복사" 가 아니라
    // "목록에서 빼서 반대편 목록에 넣는다" 로 두는 근거다.

    [Fact]
    public async Task Detach_TakesTheTabOutOfTheListWithoutFoldingIt()
    {
        var (tabs, first, second, _) = await ThreeTabsAsync();
        var folder = second.CurrentLocation;

        var detached = await tabs.DetachAsync(second).WaitAsync(Limit);

        Assert.Same(second, detached);
        Assert.DoesNotContain(second, tabs.Tabs);

        // 접히지 않았다 — 반대편 페인이 이 인스턴스를 그대로 받는다.
        Assert.Equal(folder, second.CurrentLocation);
        Assert.NotEmpty(second.Items);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task Detach_DropsTheWatchBecauseTheTabIsLeaving()
    {
        // 도착해서 활성이 될 때 다시 건다 (docs/PRD-v2.md §17). 놓지 않으면 떠난 페인의
        // 감시가 남는다.
        var (tabs, first, _, third) = await ThreeTabsAsync();
        var before = watcher.CancellationsObserved;

        await tabs.DetachAsync(third).WaitAsync(Limit);

        Assert.True(watcher.CancellationsObserved > before);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task Detach_TheLastTab_IsRefused()
    {
        // 보내면 그 페인이 탭 0개가 된다 — "페인은 항상 둘" 이라는 v1 전제가 깨진다.
        // 마지막 탭은 닫히지도 않는다는 규칙과 같은 이유다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        Assert.Null(await tabs.DetachAsync(tabs.Active).WaitAsync(Limit));
        Assert.Single(tabs.Tabs);
    }

    [Fact]
    public async Task Detach_TheActiveTab_ActivatesTheOneToItsLeft()
    {
        // 닫기와 같은 규칙이다.
        var (tabs, first, second, third) = await ThreeTabsAsync();

        await tabs.DetachAsync(third).WaitAsync(Limit);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Same(second, tabs.Active);

        await tabs.DisposeAsync();
    }

    [Fact]
    public async Task Receive_StandsRightAfterTheActiveTabAndBecomesActiveInThisPane()
    {
        // 사용자 결정 2026-08-11: 착지는 활성 탭 바로 오른쪽이고 그 페인의 활성 탭이 된다.
        var (origin, sourceFirst, _, sourceThird) = await ThreeTabsAsync();

        await using var destination = CreateTabs();
        await OpenAsync(destination, Folder(@"C:\D0", "d.txt"));
        var kept = destination.Active;
        var last = destination.NewTab();
        await destination.SwitchWork.WaitAsync(Limit);
        destination.Activate(kept);
        await destination.SwitchWork.WaitAsync(Limit);

        var moved = await origin.DetachAsync(sourceThird).WaitAsync(Limit);
        destination.Receive(moved!);
        await destination.SwitchWork.WaitAsync(Limit);

        Assert.Equal([kept, moved, last], destination.Tabs);
        Assert.Same(moved, destination.Active);

        await origin.DisposeAsync();
    }

    [Fact]
    public async Task Receive_PutsTheTabUnderThisPanesColumnsAndHiddenPolicy()
    {
        // 소유자가 바뀌었다 — 컬럼 폭과 숨김 정책은 페인의 것이다 (docs/PRD-v2.md §17).
        // 갈아입히지 않으면 건너온 탭만 반대편 페인의 폭으로 그려진다.
        var (origin, sourceFirst, _, sourceThird) = await ThreeTabsAsync();
        origin.Columns = new PaneColumns(200, 90, 120, 140);
        origin.ShowHiddenItems = false;

        await using var destination = CreateTabs();
        destination.Columns = new PaneColumns(400, 100, 130, 150);
        destination.ShowHiddenItems = true;
        await OpenAsync(destination, Folder(@"C:\D0", "d.txt"));

        var moved = await origin.DetachAsync(sourceThird).WaitAsync(Limit);
        destination.Receive(moved!);
        await destination.SwitchWork.WaitAsync(Limit);

        Assert.Equal(400, moved!.NameColumnWidth);
        Assert.True(moved.ShowHiddenItems);

        await origin.DisposeAsync();
    }

    [Fact]
    public async Task Receive_KeepsTheTabsHistoryAndSelection()
    {
        // 인스턴스를 그대로 옮기는 이유가 이것이다 — "새로 만들고 상태를 복사" 는 이 둘을
        // 하나씩 옮겨야 한다.
        var (origin, sourceFirst, _, sourceThird) = await ThreeTabsAsync();

        await sourceThird.NavigateAsync(Folder(@"C:\Deep", "x.txt"));
        sourceThird.Selection.SelectSingle("x.txt");
        Assert.True(sourceThird.CanGoBack);

        await using var destination = CreateTabs();
        await OpenAsync(destination, Folder(@"C:\D0", "d.txt"));

        var moved = await origin.DetachAsync(sourceThird).WaitAsync(Limit);
        destination.Receive(moved!);
        await destination.SwitchWork.WaitAsync(Limit);

        Assert.True(moved!.CanGoBack);
        Assert.Equal(["x.txt"], moved.Selection.SelectedNames);

        await origin.DisposeAsync();
    }

    [Fact]
    public async Task Receive_ArmsTheWatchOnTheArrivedTab()
    {
        // 옮긴 탭이 도착한 페인에서 활성이 되면 그때 감시를 건다 (docs/PRD-v2.md §17).
        var (origin, sourceFirst, _, sourceThird) = await ThreeTabsAsync();
        var folder = sourceThird.CurrentLocation!;

        await using var destination = CreateTabs();
        await OpenAsync(destination, Folder(@"C:\D0", "d.txt"));

        var moved = await origin.DetachAsync(sourceThird).WaitAsync(Limit);
        destination.Receive(moved!);
        await destination.SwitchWork.WaitAsync(Limit);

        var watching = await WatchingAsync(folder);

        source.Folders[folder] = [.. source.Folders[folder], Entry(folder, "n.txt")];
        watching.Push(new FolderChange(FolderChangeKind.Added, "n.txt"));

        await WaitForAsync(() => moved!.Items.Count == 2, "건너온 탭이 감시를 든다");

        await origin.DisposeAsync();
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
    public async Task ActivateTabCommand_MakesThePaneActiveBeforeItSwitches()
    {
        // 탭 클릭은 항목 클릭과 같은 규칙이다 — 그 페인이 활성이 된다 (docs/PRD-v2.md §17
        // 마우스 · docs/DESIGN.md §1-1). 커맨드에만 둔다: Activate 자체에 두면 반대편으로
        // 보내기(Receive)가 도착 페인을 활성으로 만들어 "원래 페인에 머문다" 가 깨진다.
        //
        // 전환보다 <b>먼저</b> 나가야 한다. 활성 탭이 바뀌면 XAML 이 무는 자리가 통째로 다시
        // 서는데, 그때 이 페인이 아직 비활성이면 새로 선 목록이 포커스를 받지 못한다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);

        var order = new List<string>();
        tabs.ActivationRequested += (_, _) => order.Add("pane");
        tabs.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PaneTabsViewModel.Active))
            {
                order.Add("tab");
            }
        };

        tabs.ActivateTabCommand.Execute(tabs.Tabs[0]);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(["pane", "tab"], order);
        Assert.NotSame(second, tabs.Active);
    }

    [Fact]
    public async Task ActivateTabCommand_OnTheTabAlreadyShowing_StillMakesThePaneActive()
    {
        // 반대편 페인의 활성 탭을 누른 경우다. 전환할 것이 없어도 클릭은 활성 전환이다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var asked = 0;
        tabs.ActivationRequested += (_, _) => asked++;

        tabs.ActivateTabCommand.Execute(tabs.Active);

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task AddTabCommand_MakesThePaneActive()
    {
        // 탭 줄의 '+' 와 빈 곳 더블클릭이다. 새 탭이 곧 활성이 되므로 페인이 비활성인 채로
        // 남으면 보고 있지 않은 페인에 탭이 생긴다.
        await using var tabs = CreateTabs();
        await OpenAsync(tabs, Folder(@"C:\A", "a.txt"));
        var asked = 0;
        tabs.ActivationRequested += (_, _) => asked++;

        tabs.AddTabCommand.Execute(null);
        await tabs.SwitchWork.WaitAsync(Limit);

        Assert.Equal(1, asked);
        Assert.Equal(2, tabs.Tabs.Count);
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

    /// <summary>
    /// 탭 셋을 세우고 <b>각자 다른 폴더</b>를 열어 둔다. 세 번째가 활성이다 — 새 탭이 활성
    /// 탭 오른쪽에 서므로 순서는 목록 그대로다.
    /// <para>
    /// 셋이 필요한 이유: 자리(왼쪽·가운데·오른쪽)가 판정에 들어가는 동작이 넷이다 —
    /// 오른쪽 탭 닫기 · 다른 탭 모두 닫기 · 닫은 뒤의 활성 · 그릴 순서.
    /// </para>
    /// </summary>
    private async Task<(PaneTabsViewModel Tabs, PaneViewModel First, PaneViewModel Second, PaneViewModel Third)>
        ThreeTabsAsync()
    {
        var tabs = CreateTabs();
        var first = tabs.Active;
        await first.NavigateAsync(Folder(@"C:\T0", "t0.txt"));

        var second = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await second.NavigateAsync(Folder(@"C:\T1", "t1.txt"));

        var third = tabs.NewTab();
        await tabs.SwitchWork.WaitAsync(Limit);
        await third.NavigateAsync(Folder(@"C:\T2", "t2.txt"));

        return (tabs, first, second, third);
    }

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
        // 3000회 × 10ms = 30초. 5초였다가 올렸다 (2026-08-24) — 전체 병렬 실행의 부하에서
        // Reactivating_RefreshesOnceThenWatchesAgain 이 사흘 만에 두 번 그 선을 넘었고,
        // 단독으로는 같은 판정이 59~142ms 에 난다. **상한은 마감이 아니라 매달림을 실패로
        // 바꾸는 장치이므로 실제 동작 시간과 100배쯤 벌어져 있어야 한다** (위 요약).
        // 진짜 매달림은 게이트 바깥의 --blame-hang-timeout 120s 가 다시 받는다.
        for (var attempt = 0; attempt < 3000; attempt++)
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
