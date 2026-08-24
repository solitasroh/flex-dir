using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.Core.Locations;
using FlexDir.Core.Tools;
using FlexDir.Core.ViewState;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 닫힌 탭의 기록 (docs/PRD-v2.md §17 되살리기). <b>인스턴스가 아니라 기록이다</b>
/// (사용자 결정 2026-08-11) — 닫은 탭을 살려 두면 메모리가 "항목 수 × 탭 수" 에 닫힌 탭
/// 10개만큼 더 붙고 (ADR-018 대가), 그 탭들의 썸네일 스케줄러·열거 세션이 종료 순서에
/// 하나 더 얹힌다.
/// </summary>
/// <param name="Index">닫히기 전에 서 있던 자리. 되살리기가 그 자리로 되돌린다.</param>
public sealed record ClosedTab(int Index, TabState State);

/// <summary>
/// 페인 하나의 탭 목록과 활성 탭 (docs/PRD-v2.md §17 · ADR-018 · docs/ARCHITECTURE.md §4).
/// <para>
/// <b>탭 하나가 <see cref="PaneViewModel"/> 하나다.</b> 전환이 즉시여야 탭이 왕복 비용을
/// 줄인다 — 위치만 기억하고 전환마다 재열거하면 NAS(185ms)·WSL(항목당 250ms)에서 탭 전환이
/// 폴더 이동과 같은 값이 되어 존재 이유가 없어진다.
/// </para>
/// <para>
/// <b>그러나 감시는 활성 탭만 든다</b> (<see cref="Activate"/>). 근거는 docs/PRD-v2.md §13
/// 전체다 — 감시 오버플로 폭주는 이 저장소가 실물에서 값을 치른 사건이고, 탭 8개가 각자
/// 감시하면 그것이 8배로 돌아온다. 게다가 폭주한 탭이 화면에 없으면 상태표시줄 문구조차
/// 보이지 않는 자리에서 뜬다.
/// </para>
/// <para>
/// <b>컬럼 폭·숨김 정책·터미널 선택의 소유자가 여기다</b> — 탭이 아니다 (docs/PRD-v2.md §17 구조 렌즈).
/// 탭 하나가 인스턴스 하나이므로 소유자를 올리지 않으면 탭 전환마다 컬럼이 튀고, 나중에
/// 만든 탭이 옛 숨김 정책으로 뜬다.
/// </para>
/// <para>
/// <b>포트를 들지 않는다.</b> 페인을 만드는 법은 <see cref="Func{TResult}"/> 하나로 받는다 —
/// 그래서 App 은 여전히 <c>Shell</c> 을 모른다 (CLAUDE.md §1). 조립은
/// <c>AppComposition.Create</c> 의 <c>Pane()</c> 지역 함수가 그대로 그 팩토리가 된다.
/// </para>
/// </summary>
public sealed partial class PaneTabsViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Func<PaneViewModel> createPane;
    private readonly ObservableCollection<PaneViewModel> tabs = [];

    private PaneViewModel active;
    private PaneColumns columns = PaneColumns.Default;
    private bool showHiddenItems;
    private TerminalChoice terminal = new(TerminalPreset.WindowsTerminal);
    private bool disposed;

    /// <summary>
    /// 그릴 순서. 목록이나 고정이 바뀌면 버린다 — <c>PaneViewModel.Rows</c> 와 같은 수다.
    /// </summary>
    private IReadOnlyList<PaneViewModel>? stripTabs;

    /// <summary>
    /// 컬럼 폭을 탭들에 미는 중. 페인이 값을 밀면 탭마다 알림이 넷 오는데, 그것을 다시
    /// "사용자가 끌었다" 로 읽으면 되먹임이 된다.
    /// </summary>
    private bool pushingColumns;

    /// <summary>
    /// 진행 중인 탭 전환의 취소원. <b>전환마다 앞선 것을 취소한다</b> — 탭을 빠르게 오가면
    /// 이전 탭의 해제와 새 탭의 구독이 경합한다 (docs/PRD-v2.md §17 구조 렌즈). 트리
    /// 따라가기가 이미 같은 수를 쓴다 (<c>FolderTreeViewModel.RevealAsync</c>).
    /// </summary>
    private CancellationTokenSource? switching;

    private Task switchWork = Task.CompletedTask;

    /// <param name="paneFactory">
    /// 탭 하나를 만드는 법. 포트 열두 개를 묶는 자리가 <c>Host</c> 에 이미 있다.
    /// </param>
    public PaneTabsViewModel(Func<PaneViewModel> paneFactory)
    {
        ArgumentNullException.ThrowIfNull(paneFactory);

        createPane = paneFactory;
        Tabs = new ReadOnlyObservableCollection<PaneViewModel>(tabs);

        // 탭 줄은 탭이 1개여도 항상 보인다 (docs/PRD-v2.md §17). 그래서 "탭 0개" 는 어느
        // 시점에도 존재하지 않는다 — 조립 직후부터 하나가 서 있다.
        active = Add(index: 0);
    }

    /// <summary>
    /// 이 페인의 탭들. 전부 살아 있다 (ADR-018). 순서가 곧 탭 줄의 순서다.
    /// <para>
    /// 읽기 전용으로 낸다 — 넣고 빼는 자리가 여기 하나여야 활성 탭·감시 구독·컬럼 공유가
    /// 함께 따라간다.
    /// </para>
    /// </summary>
    public ReadOnlyObservableCollection<PaneViewModel> Tabs { get; }

    /// <summary>화면에 보이는 탭. <b>감시를 드는 유일한 탭이다.</b></summary>
    public PaneViewModel Active
    {
        get => active;
        private set
        {
            if (ReferenceEquals(active, value))
            {
                return;
            }

            active = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveIndex));
            OnPropertyChanged(nameof(CanCloseActive));
        }
    }

    /// <summary>활성 탭의 자리. 저장 포맷이 이 번호를 기억한다 (docs/PRD-v2.md §17).</summary>
    public int ActiveIndex => tabs.IndexOf(active);

    /// <summary>
    /// <b>탭 줄이 그릴 순서</b> — 고정 탭이 앞에 모이고, 그 안에서는 목록 순서다
    /// (docs/DESIGN.md §1-1 · 사용자 결정 2026-08-11).
    /// <para>
    /// <b>고정은 <see cref="Tabs"/> 를 건드리지 않는다.</b> 그래서 고정을 풀면 원래 자리로
    /// 돌아가고 저장 포맷의 순서도 흔들리지 않는다 — 대가는 "그릴 순서" 와 "목록 순서" 가
    /// 갈리는 것이고, 그 변환이 여기 한 곳에 있다.
    /// </para>
    /// <para>
    /// 정렬을 XAML 에 두지 않는 이유: 같은 규칙이 두 계층으로 갈린다. 자리를 판정하는
    /// 동작(<see cref="CloseToTheRightAsync"/>)도 이 순서를 봐야 한다.
    /// </para>
    /// </summary>
    public IReadOnlyList<PaneViewModel> StripTabs => stripTabs ??= BuildStripTabs();

    /// <summary>
    /// 활성 탭을 닫을 수 있는가. 마지막 탭과 고정 탭은 닫히지 않는다
    /// (docs/PRD-v2.md §17) — View 가 닫기 버튼을 감추는 근거다.
    /// </summary>
    public bool CanCloseActive => CanClose(active);

    /// <summary>
    /// Details 컬럼의 폭. <b>페인의 모든 탭이 이것을 공유한다</b>
    /// (docs/PRD-v2.md §17 구조 렌즈).
    /// <para>
    /// §11 이 "페인마다 따로" 로 정한 결론은 그대로 산다 — 좌·우는 여전히 각자의 폭을
    /// 갖고, 갈리지 않는 것은 <b>한 페인 안의 탭들</b>이다.
    /// </para>
    /// </summary>
    public PaneColumns Columns
    {
        get => columns;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (columns.Equals(value))
            {
                return;
            }

            columns = value;
            PushColumns();

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 숨김·시스템 항목을 목록에 낼 것인가 (docs/PRD-v2.md §12). <b>소유자가 여기다</b> —
    /// 워크스페이스가 페인 둘에만 밀던 시절에는 나중에 만든 탭이 기본값으로 떴다
    /// (docs/PRD-v2.md §17 구조 렌즈).
    /// <para>
    /// <b>바뀌었다고 스스로 다시 읽지 않는다.</b> 다시 읽을지를 정하는 곳은 여전히
    /// <c>WorkspaceViewModel</c> 하나다 (<see cref="PaneViewModel.ShowHiddenItems"/> 와 같은
    /// 이유). 배경 탭은 활성이 될 때의 새로 고침이 새 정책으로 다시 읽는다.
    /// </para>
    /// </summary>
    public bool ShowHiddenItems
    {
        get => showHiddenItems;
        set
        {
            if (showHiddenItems == value)
            {
                return;
            }

            showHiddenItems = value;

            foreach (var tab in tabs)
            {
                tab.ShowHiddenItems = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 터미널 버튼이 열 것 (docs/PRD-v2.md §20). <b>소유자가 여기인 이유는
    /// <see cref="ShowHiddenItems"/> 와 같다</b> — 탭에 두면 나중에 만든 탭이 기본 프리셋으로
    /// 뜨고 아무도 다시 밀지 않는다. 값은 <c>WorkspaceViewModel</c> 이 설정에서 밀어 넣는다.
    /// </summary>
    public TerminalChoice Terminal
    {
        get => terminal;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            // record 라 값 비교다. 같은 값을 거르지 않으면 설정 창을 닫을 때마다 탭 수만큼
            // 레지스트리 조회가 나간다 (PaneViewModel.Terminal 이 바뀌면 다시 찾는다).
            if (terminal == value)
            {
                return;
            }

            terminal = value;

            foreach (var tab in tabs)
            {
                tab.Terminal = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 어느 탭에서든 항목을 클릭했다 — 그 페인이 활성이 되어야 한다 (docs/DESIGN.md §6).
    /// 탭이 여럿으로 늘어도 워크스페이스가 무는 곳은 여전히 페인 둘이다.
    /// </summary>
    public event EventHandler? ActivationRequested;

    /// <summary>목록 우클릭의 '즐겨찾기에 추가' (docs/PRD-v2.md §10-2). 탭에서 그대로 나른다.</summary>
    public event EventHandler<IReadOnlyList<string>>? PinRequested;

    /// <summary>
    /// 탭 하나가 닫혔다 — 되살리기 스택이 이 신호로 채워진다 (docs/PRD-v2.md §17).
    /// <para>
    /// <b>닫는 길이 다섯이라 신호가 하나여야 한다</b> — <c>Ctrl+W</c> · 가운데 버튼 ·
    /// 컨텍스트 메뉴의 닫기 셋. 각 경로가 스택에 직접 쌓으면 하나를 빼먹는 순간 그 탭은
    /// 되살아나지 않고, 어느 경로가 빠졌는지는 실물에서만 드러난다.
    /// </para>
    /// </summary>
    public event EventHandler<ClosedTab>? TabClosed;

    /// <summary>
    /// 이 페인이 보는 폴더가 바뀌었다 — <b>활성 탭의 이동과 탭 전환 둘 다</b>다. 트리가
    /// 따라가는 근거이며 (docs/PRD-v2.md §10-3) 탭 전환도 같은 자리를 지나야 한다
    /// (docs/PRD-v2.md §17 자명하게).
    /// </summary>
    public event EventHandler? ActiveLocationChanged;

    /// <summary>진행 중인 탭 전환. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    internal Task SwitchWork => switchWork;

    /// <summary>
    /// 새 탭 (<c>Ctrl+T</c> · <c>+</c>). <b>활성 탭 바로 오른쪽</b>에 서고 즉시 활성이 되며
    /// 지금 보는 폴더를 복제한다 (사용자 결정 2026-08-11 · docs/PRD-v2.md §17).
    /// <para>
    /// 맨 뒤가 아닌 이유: 복제라 제목이 같은 탭 둘이 나란히 서는데, 방금 생긴 것이 어느
    /// 쪽인지 보이고 원본으로 돌아가는 길이 바로 왼쪽이어야 한다.
    /// </para>
    /// <para>
    /// 폴더를 여는 것은 <see cref="SwitchWork"/> 에서 돈다 — 여기서 기다리면 느린 경로에서
    /// 탭이 서기까지 초 단위가 걸린다 (CLAUDE.md §3).
    /// </para>
    /// </summary>
    public PaneViewModel NewTab()
    {
        var tab = Add(ActiveIndex + 1);

        // 위치만 실어 둔다. 여는 시점은 활성이 되는 순간이고, 그 경로는 배경 탭이 처음
        // 활성이 될 때와 완전히 같다 (Activate).
        tab.PendingLocation = active.CurrentLocation ?? active.PendingLocation;

        Activate(tab);

        return tab;
    }

    /// <summary>
    /// 되살린 탭을 원래 자리에 끼운다 (docs/PRD-v2.md §17). 끼운 탭이 활성이 된다 —
    /// 방금 그것을 원해서 누른 키다 (사용자 렌즈가 잡은 것).
    /// <para>
    /// 자리가 목록 밖이면 맨 뒤다. 되살리는 사이에 탭을 여럿 닫았을 수 있고, 그때 던지면
    /// 조작이 통째로 사라진다.
    /// </para>
    /// </summary>
    public PaneViewModel Insert(int index, TabState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var tab = Add(index < 0 ? 0 : index > tabs.Count ? tabs.Count : index);

        tab.IsPinned = state.IsPinned;
        tab.CustomTitle = state.Title;
        tab.PendingLocation = state.Folder;

        Activate(tab);

        return tab;
    }

    /// <summary>
    /// 탭을 닫는다 (<c>Ctrl+W</c> · 가운데 버튼). <b>마지막 탭과 고정 탭은 닫히지 않는다</b>
    /// (docs/PRD-v2.md §17) — 그때 <see langword="null"/> 을 낸다.
    /// <para>
    /// 닫힌 탭의 인스턴스는 접는다. 되살리기가 드는 것은 돌려주는 <see cref="ClosedTab"/>
    /// 기록뿐이다.
    /// </para>
    /// <para>
    /// 활성 탭을 닫으면 <b>왼쪽 탭</b>이 활성이 된다 (사용자 결정 2026-08-11). 새 탭이
    /// 오른쪽에 서므로, 잠깐 다녀온 탭을 닫으면 출발한 탭으로 돌아온다 — 두 규칙이 짝이다.
    /// 왼쪽이 없으면 오른쪽이다.
    /// </para>
    /// </summary>
    public async Task<ClosedTab?> CloseAsync(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!CanClose(tab))
        {
            return null;
        }

        var index = tabs.IndexOf(tab);

        if (index < 0)
        {
            // 이 페인의 탭이 아니다. 던지지 않는다 — 페인 간 이동이 들어오면 같은 탭에
            // 대해 두 페인이 이 경로를 지날 수 있다.
            return null;
        }

        // 감시를 <b>여기서</b> 놓는다. 아래 전환에 맡기면 그쪽이 배경에서 도는 동안 이 탭이
        // 접히고, 그러면 닫기가 "접었다" 를 보장하지 못한다 — 감시 루프가 이미 없는 탭을
        // 훑고 있는 상태로 남는다.
        await tab.SuspendWatchAsync().ConfigureAwait(false);

        // 활성 탭이면 이웃으로 먼저 옮긴다. 목록에서 빼고 나서 고르면 자리를 잃는다.
        if (ReferenceEquals(tab, active))
        {
            Activate(tabs[index == 0 ? 1 : index - 1]);
        }

        tabs.RemoveAt(index);
        Abandon(tab);

        InvalidateStrip();
        OnPropertyChanged(nameof(ActiveIndex));
        OnPropertyChanged(nameof(CanCloseActive));

        var state = Capture(tab);

        await tab.DisposeAsync().ConfigureAwait(false);

        // 폴더를 한 번도 열지 못한 탭은 되살릴 것이 없다.
        if (state is null)
        {
            return null;
        }

        var closed = new ClosedTab(index, state);

        TabClosed?.Invoke(this, closed);

        return closed;
    }

    /// <summary>
    /// 고정을 켜고 끈다 (컨텍스트 메뉴 · docs/PRD-v2.md §17). <b>목록 순서는 건드리지
    /// 않는다</b> (사용자 결정 2026-08-11) — 모이는 것은 그릴 때뿐이다
    /// (<see cref="StripTabs"/>).
    /// </summary>
    public void TogglePin(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (tabs.Contains(tab))
        {
            // 알림은 탭이 낸다. 그것을 받아 그릴 순서를 버리는 것은 OnTabPropertyChanged 다.
            tab.IsPinned = !tab.IsPinned;
        }
    }

    /// <summary>
    /// 탭을 복제한다 (컨텍스트 메뉴). <b>복제한 탭 바로 오른쪽</b>에 서고 활성이 된다 —
    /// 기준이 활성 탭이 아니라 우클릭한 탭이라는 점만 <see cref="NewTab"/> 과 다르다.
    /// <para>
    /// <b>따라오는 것은 폴더뿐이다.</b> 고정과 사용자 제목은 따라오지 않는다 — <c>Ctrl+T</c>
    /// 가 복제하는 것도 폴더이고 (docs/PRD-v2.md §17), 제목까지 따라오면 같은 이름 둘이 서서
    /// 어느 쪽이 원본인지 알 수 없다.
    /// </para>
    /// </summary>
    public PaneViewModel Duplicate(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var index = tabs.IndexOf(tab);
        var copy = Add(index < 0 ? tabs.Count : index + 1);

        copy.PendingLocation = tab.CurrentLocation ?? tab.PendingLocation;

        Activate(copy);

        return copy;
    }

    /// <summary>
    /// 그 탭만 남기고 닫는다 (컨텍스트 메뉴). <b>고정 탭은 남는다</b> —
    /// <see cref="CanClose"/> 가 그것을 거부한다.
    /// <para>
    /// 남는 탭이 활성이 된다. 나머지가 전부 사라지므로 다른 결말이 없다.
    /// </para>
    /// </summary>
    public Task CloseOthersAsync(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!tabs.Contains(tab))
        {
            return Task.CompletedTask;
        }

        Activate(tab);

        return CloseAllAsync([.. tabs.Where(candidate => !ReferenceEquals(candidate, tab))]);
    }

    /// <summary>
    /// 그 탭 오른쪽을 모두 닫는다 (컨텍스트 메뉴).
    /// <para>
    /// <b>"오른쪽" 은 <see cref="StripTabs"/> 기준이다</b> — 사용자가 보는 것이 그 순서다.
    /// 목록 기준과 갈리는 경우는 하나뿐이지만 실재한다: 고정 탭을 우클릭하면 그 왼쪽에
    /// 그려진 탭이 목록에서는 앞에 있어 "오른쪽" 판정이 뒤집힌다.
    /// </para>
    /// </summary>
    public Task CloseToTheRightAsync(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var order = StripTabs;
        var at = -1;

        for (var index = 0; index < order.Count; index++)
        {
            if (ReferenceEquals(order[index], tab))
            {
                at = index;

                break;
            }
        }

        return at < 0 ? Task.CompletedTask : CloseAllAsync([.. order.Skip(at + 1)]);
    }

    /// <summary>
    /// 탭을 이 페인에서 떼어낸다 — <b>접지 않는다</b>. 반대편 페인이 이 인스턴스를 그대로
    /// 받는다 (docs/PRD-v2.md §17 페인 간 이동 · <see cref="Receive"/>).
    /// <para>
    /// <b>마지막 탭은 떼어낼 수 없다</b> — 그러면 이 페인이 탭 0개가 되어 "페인은 항상 둘"
    /// 이라는 v1 전제가 깨진다. 마지막 탭이 닫히지 않는 것과 같은 이유다. 그때
    /// <see langword="null"/> 을 낸다.
    /// </para>
    /// <para>
    /// <b>고정 탭은 떼어낼 수 있다</b> — 고정은 "닫히지 않는다" 이지 "움직이지 않는다" 가
    /// 아니고, 고정 여부는 인스턴스에 붙어 있어 건너간 뒤에도 그대로다.
    /// </para>
    /// <para>
    /// 감시는 여기서 놓는다. 도착한 페인에서 활성이 될 때 다시 걸린다.
    /// </para>
    /// </summary>
    public async Task<PaneViewModel?> DetachAsync(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var index = tabs.IndexOf(tab);

        if (index < 0 || tabs.Count < 2)
        {
            return null;
        }

        await tab.SuspendWatchAsync().ConfigureAwait(false);

        // 활성 탭이면 이웃으로 먼저 옮긴다 — 닫기와 같은 규칙(왼쪽)이다.
        if (ReferenceEquals(tab, active))
        {
            Activate(tabs[index == 0 ? 1 : index - 1]);
        }

        tabs.RemoveAt(index);
        Abandon(tab);

        InvalidateStrip();
        OnPropertyChanged(nameof(ActiveIndex));
        OnPropertyChanged(nameof(CanCloseActive));

        return tab;
    }

    /// <summary>
    /// 반대편 페인에서 건너온 탭을 받는다 (docs/PRD-v2.md §17). <b>활성 탭 바로 오른쪽</b>에
    /// 서고 <b>이 페인의 활성 탭이 된다</b> (사용자 결정 2026-08-11) — 그러면서 감시가 다시
    /// 걸린다.
    /// <para>
    /// <b>정책은 이 페인 것으로 갈아입는다.</b> 컬럼 폭과 숨김 정책의 소유자가 페인이므로
    /// (docs/PRD-v2.md §17 구조 렌즈), 갈아입히지 않으면 건너온 탭만 반대편 페인의 폭으로
    /// 그려진다.
    /// </para>
    /// <para>
    /// 히스토리·선택·열거 세션·썸네일 스케줄러는 인스턴스에 붙어 있어 <b>손대지 않아도</b>
    /// 따라온다. 그것이 "새로 만들고 상태를 복사" 가 아니라 인스턴스를 옮기는 근거다.
    /// </para>
    /// </summary>
    public void Receive(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (tabs.Contains(tab))
        {
            return;
        }

        Adopt(tab, ActiveIndex + 1);
        Activate(tab);
    }

    /// <summary>
    /// 이 페인의 탭을 반대편 페인으로 넘긴다 — <see cref="DetachAsync"/> 와
    /// <see cref="Receive"/> 를 한 쌍으로 묶는다 (docs/PRD-v2.md §17 페인 간 이동).
    /// <para>
    /// <b>부르는 곳이 둘이라 짝이 한 자리에 있어야 한다</b> — 컨텍스트 메뉴 '반대편 페인으로
    /// 보내기'(<c>WorkspaceViewModel</c>)와 탭 드래그(<c>Views/TabDragInput.cs</c>). 각자
    /// 순서를 쓰면 한쪽이 떼기의 <see langword="null"/>(마지막 탭)을 빠뜨리는 순간 그 탭이
    /// 어느 페인에도 없는 채로 사라진다.
    /// </para>
    /// <para>
    /// 마지막 탭이면 아무 일도 없다 — 그 판정은 <see cref="DetachAsync"/> 가 한다.
    /// </para>
    /// </summary>
    public async Task SendAsync(PaneViewModel tab, PaneTabsViewModel destination)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(destination);

        if (await DetachAsync(tab).ConfigureAwait(false) is { } moved)
        {
            destination.Receive(moved);
        }
    }

    /// <summary>
    /// 같은 페인 안에서 탭 순서를 바꾼다 (탭 드래그 · docs/PRD-v2.md §17).
    /// <para>
    /// <b>자리는 <see cref="StripTabs"/> 기준이다</b> — 사용자가 끌어다 놓는 자리가 그것이다.
    /// 목록 기준으로 받으면 고정 탭이 있는 페인에서 손이 놓은 곳과 다른 자리에 선다.
    /// </para>
    /// <para>
    /// <b>무리를 건너뛰지 않는다.</b> 고정 탭은 줄 맨 왼쪽에 모이므로, 비고정 탭을 그 앞에
    /// 놓아도 그려지는 자리는 고정 탭 뒤다 — 자르지 않으면 목록만 바뀌고 화면은 그대로여서
    /// "놓은 자리에 안 간다" 로 보인다. 결정이 아니라 고정 규칙의 적용이다.
    /// </para>
    /// </summary>
    public void MoveTab(PaneViewModel tab, int stripIndex)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var from = tabs.IndexOf(tab);

        if (from < 0)
        {
            // 이 페인의 탭이 아니다 — 반대편에서 끌어온 것이면 그것은 소유권 이동이고
            // (DetachAsync/Receive) 이 길이 아니다. 던지지 않는다.
            return;
        }

        List<PaneViewModel> group = [.. tabs.Where(candidate => candidate.IsPinned == tab.IsPinned)];

        // 비고정 무리는 고정 탭 개수만큼 뒤에서 시작한다 — 그것이 StripTabs 의 모양이다.
        var start = tab.IsPinned ? 0 : tabs.Count - group.Count;
        var to = tabs.IndexOf(group[Math.Clamp(stripIndex - start, 0, group.Count - 1)]);

        if (from == to)
        {
            return;
        }

        tabs.Move(from, to);

        InvalidateStrip();
        OnPropertyChanged(nameof(ActiveIndex));
    }

    /// <summary>다음 탭 (<c>Ctrl+Tab</c> · <c>Ctrl+PageDown</c>). 이 페인 안에서 순환한다.</summary>
    public void Next() => Step(1);

    /// <summary>이전 탭 (<c>Ctrl+Shift+Tab</c> · <c>Ctrl+PageUp</c>).</summary>
    public void Previous() => Step(-1);

    /// <summary>
    /// 그 탭으로 전환한다. <b>화면은 즉시 바뀌고 감시만 배경에서 옮겨간다</b> — 전환이
    /// 즉시여야 탭이 왕복 비용을 줄인다 (ADR-018).
    /// <para>
    /// 배경에서 도는 것은 셋이다. (1) 떠나는 탭이 감시를 놓는다. (2) 오는 탭이 아직 폴더를
    /// 열지 않았으면 (복원된 배경 탭·방금 만든 탭) 그것을 연다. (3) 이미 열었으면
    /// <b>새로 고침 한 번</b>을 돌린다 — 배경에 있는 동안 놓친 외부 변경을 그것이 메우고,
    /// 그 새로 고침이 감시를 다시 건다.
    /// </para>
    /// </summary>
    public void Activate(PaneViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (ReferenceEquals(tab, active) || !tabs.Contains(tab))
        {
            return;
        }

        var leaving = active;

        Active = tab;

        // 앞선 전환을 끊는다. 이 취소는 <b>새로 고침만</b> 막는다 — 놓기는 취소하지 않는다
        // (SwitchAsync 의 주석).
        var next = new CancellationTokenSource();

        Interlocked.Exchange(ref switching, next)?.Cancel();

        switchWork = SwitchAsync(leaving, tab, next.Token);

        // 탭 전환도 "이 페인이 보는 폴더가 바뀌었다" 다 — 트리가 따라간다.
        ActiveLocationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 이 페인을 접는다 — 분할을 줄일 때다 (docs/PRD-v2.md §18 · 사용자 결정 2026-08-12).
    /// <para>
    /// <b>인스턴스는 살아 있고 감시만 놓는다.</b> 그래서 다시 폈을 때 탭도 선택도 스크롤도
    /// 그대로 돌아온다 — 접기는 닫기가 아니다. 대가는 접힌 페인의 탭들이 메모리에 남는
    /// 것이고 (ADR-018 이 이미 치른 값), 얻는 것은 §13 이 값을 치르고 배운 것이다:
    /// <b>화면에 없는 것이 감시를 들면 폭주가 보이지 않는 자리에서 돈다.</b>
    /// </para>
    /// </summary>
    public Task SuspendAsync()
    {
        // 진행 중인 전환의 '여는' 쪽을 끊는다. 그러지 않으면 접는 중에 새로 고침이 감시를
        // 다시 건다 (DisposeAsync 와 같은 수).
        Interlocked.Exchange(ref switching, null)?.Cancel();

        switchWork = active.SuspendWatchAsync();

        return switchWork;
    }

    /// <summary>
    /// 접었던 페인을 편다 — 활성 탭이 자기 폴더를 다시 연다 (docs/PRD-v2.md §18).
    /// <para>
    /// <b>한 번도 접힌 적 없는 새 페인도 이 길로 선다.</b> 그때 활성 탭은
    /// <c>PendingLocation</c> 만 든 상태이고, 그것을 여는 규칙은 배경 탭이 처음 활성이 될
    /// 때와 완전히 같다 (<see cref="OpenAsync"/>).
    /// </para>
    /// </summary>
    public Task ResumeAsync(CancellationToken ct = default)
    {
        var next = new CancellationTokenSource();

        Interlocked.Exchange(ref switching, next)?.Cancel();

        switchWork = OpenAsync(active, next.Token);

        // 편 페인이 보는 폴더는 트리가 따라갈 자리다 — 탭 전환과 같은 사건이다.
        ActiveLocationChanged?.Invoke(this, EventArgs.Empty);

        return switchWork;
    }

    /// <summary>
    /// 저장된 탭 목록을 세운다 (docs/PRD-v2.md §17). <b>여는 것은 활성 탭 하나뿐이다</b> —
    /// 배경 탭은 위치만 들고 있다가 처음 활성화될 때 연다. 그것이 cold start 를 지금과 같이
    /// 두는 자리다 (ADR-018 · ADR-016).
    /// </summary>
    /// <param name="state">
    /// 기억된 탭 목록. <see langword="null"/> 이거나 비어 있으면 탭 하나로 시작한다.
    /// </param>
    /// <param name="activeFolder">
    /// 활성 탭이 열 폴더. <b>기억된 폴더가 아니라 시작 폴더 규칙을 지난 결과다</b>
    /// (<c>AppSettings.ResolveStartFolder</c>) — 그 규칙을 아는 곳은 워크스페이스다.
    /// <see langword="null"/> 이면 아무 곳도 열지 않는다 (첫 실행에 폴백조차 없는 경우).
    /// </param>
    /// <param name="open">
    /// 활성 탭의 폴더를 지금 열 것인가 (docs/PRD-v2.md §18). <b>접힌 채로 복원되는 페인은
    /// 열지 않는다</b> — 그것이 열면 화면에 없는 페인이 감시를 들고, §13 의 폭주가 보이지
    /// 않는 자리에서 시작할 수 있다. 그때 폴더는 배경 탭과 같이 <c>PendingLocation</c> 으로
    /// 실리고, 펴는 순간 <see cref="ResumeAsync"/> 가 연다.
    /// </param>
    public Task RestoreAsync(
        PaneTabsState? state,
        LocationId? activeFolder,
        bool open = true,
        CancellationToken ct = default)
    {
        var restored = state?.Tabs ?? [];

        // 조립이 만들어 둔 탭 하나를 활성 자리에 그대로 쓴다. 버리고 새로 만들면 그 인스턴스가
        // 접히지 않은 채 남거나, 접는 순서가 복원 경로에 얹힌다.
        var activeIndex = restored.Count == 0 ? 0 : state!.ActiveIndex;

        for (var index = 0; index < restored.Count; index++)
        {
            var tab = index == activeIndex ? active : Add(tabs.Count);

            tab.IsPinned = restored[index].IsPinned;
            tab.CustomTitle = restored[index].Title;

            // 활성 탭의 폴더는 아래에서 곧바로 연다. 배경 탭은 위치만 든다.
            if (index != activeIndex)
            {
                tab.PendingLocation = restored[index].Folder;
            }
        }

        // 조립이 만든 탭은 목록의 0번이라, 활성 자리가 0번이 아니면 그 자리로 옮긴다.
        if (activeIndex > 0 && activeIndex < tabs.Count)
        {
            tabs.Move(0, activeIndex);

            InvalidateStrip();
            OnPropertyChanged(nameof(ActiveIndex));
        }

        if (activeFolder is null)
        {
            return Task.CompletedTask;
        }

        if (!open)
        {
            // 접힌 채로 선다. 배경 탭과 같은 모양이고, 펴는 순간 같은 길로 열린다.
            active.PendingLocation = activeFolder;

            return Task.CompletedTask;
        }

        return active.NavigateAsync(activeFolder, ct);
    }

    /// <summary>
    /// 지금의 탭 목록을 저장할 모양으로 낸다 (docs/PRD-v2.md §17).
    /// <para>
    /// <b>폴더를 아직 열지 않은 배경 탭도 담긴다</b> — 그것이 담기지 않으면 앱을 두 번
    /// 켜는 것만으로 배경 탭이 사라진다. 폴더가 아예 없는 탭(열지 못한 새 탭)은 빠지고,
    /// 그러면 활성 탭 번호도 함께 당겨진다.
    /// </para>
    /// <para>
    /// 담을 것이 하나도 없으면 <see langword="null"/> 이다 — "탭 0개" 를 저장하면 다음
    /// 실행이 그것을 기억으로 받아 시작 폴더 규칙을 지나지 못한다.
    /// </para>
    /// </summary>
    public PaneTabsState? Capture()
    {
        var states = new List<TabState>(tabs.Count);

        // 활성 탭이 어느 것인지는 번호가 아니라 인스턴스로 센다. 앞의 탭이 빠지면 그 뒤가
        // 전부 하나씩 당겨지기 때문이다 (JsonViewStateStore.ToTabs 와 같은 수).
        var activeIndex = 0;

        foreach (var tab in tabs)
        {
            if (Capture(tab) is not { } state)
            {
                continue;
            }

            if (ReferenceEquals(tab, active))
            {
                activeIndex = states.Count;
            }

            states.Add(state);
        }

        return states.Count == 0 ? null : new PaneTabsState(states, activeIndex);
    }

    /// <summary>
    /// 모든 탭을 접는다 (ADR-018 대가). 배경 탭은 감시를 이미 놓았지만 썸네일 스케줄러와
    /// 열거 세션은 탭마다 살아 있다.
    /// <para>
    /// <b>shell 구현체를 닫기 전에 여기가 끝나야 한다</b> — 순서를 뒤집으면 진행 중인
    /// 썸네일·유형 이름 요청이 닫힌 STA 큐에 들어간다 (<c>AppComposition.DisposeAsync</c>).
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // 진행 중인 전환을 먼저 끊는다. 그러지 않으면 접는 중에 새로 고침이 감시를 다시
        // 건다.
        Interlocked.Exchange(ref switching, null)?.Cancel();

        try
        {
            await switchWork.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // <b>취소도 삼킨다.</b> 방금 위에서 끊은 것이 이 전환이므로 취소는 정상 종료이고,
            // 그것을 밖으로 내면 <c>AppComposition.DisposeAsync</c> 가 shell 구현체를 닫기
            // 전에 터져 STA 워커가 프로세스에 남는다 (Host.Tests 가 이것을 잡았다).
            //
            // 다른 실패도 마찬가지다 — 전환의 실패 사유는 이미 그 탭의 상태표시줄에 있고
            // (PaneViewModel.FillAsync) 여기서 다시 내면 종료가 막힌다.
        }

        foreach (var tab in tabs)
        {
            await tab.DisposeAsync().ConfigureAwait(false);
        }
    }

    // 아래 얇은 커맨드들은 탭 줄의 클릭·가운데 버튼·컨텍스트 메뉴가 문다 — 그 자리는
    // ICommand 만 받는다 (PaneViewModel 의 커맨드들과 같은 자리). 판단은 전부 위의 메서드
    // 안에 있고, 여기서 하는 일은 <c>null</c> 을 걸러내는 것뿐이다: 커맨드 매개변수는
    // 바인딩이 아직 서지 않은 순간에 <c>null</c> 로 온다.

    /// <summary>
    /// 탭 줄의 탭을 눌렀다. <b>그 페인이 활성이 된다</b> — 항목 클릭과 같은 규칙이다
    /// (docs/PRD-v2.md §17 마우스 · docs/DESIGN.md §1-1).
    /// <para>
    /// 활성 전환을 <see cref="Activate"/> 가 아니라 <b>여기</b>에 두는 이유: 전환은 여러
    /// 곳에서 불리고 그중 <see cref="Receive"/> 는 도착 페인이 활성이 되면 안 된다
    /// (사용자 결정 2026-08-11 — 보내는 것은 정리 동작이라 하던 페인에 남는다).
    /// </para>
    /// <para>
    /// 전환보다 <b>먼저</b> 낸다. 활성 탭이 바뀌면 XAML 이 무는 자리가 통째로 다시 서는데,
    /// 그때 이 페인이 아직 비활성이면 새로 선 목록이 포커스를 받지 못한다
    /// (<c>Views/TabFocus.cs</c>).
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ActivateTab(PaneViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        ActivationRequested?.Invoke(this, EventArgs.Empty);

        Activate(tab);
    }

    /// <summary>
    /// 탭 줄의 <c>+</c> 와 빈 곳 더블클릭 (docs/PRD-v2.md §17 마우스). 새 탭이 곧 활성이
    /// 되므로 <b>그 페인도 활성이 된다</b> — 아니면 보고 있지 않은 페인에 탭이 생긴다.
    /// </summary>
    [RelayCommand]
    private void AddTab()
    {
        ActivationRequested?.Invoke(this, EventArgs.Empty);

        NewTab();
    }

    [RelayCommand]
    private void TogglePinOnTab(PaneViewModel? tab)
    {
        if (tab is not null)
        {
            TogglePin(tab);
        }
    }

    [RelayCommand]
    private void DuplicateTab(PaneViewModel? tab)
    {
        if (tab is not null)
        {
            Duplicate(tab);
        }
    }

    /// <summary>탭 가운데 버튼과 컨텍스트 메뉴의 '닫기'.</summary>
    [RelayCommand]
    private Task CloseTabAsync(PaneViewModel? tab)
        => tab is null ? Task.CompletedTask : CloseAsync(tab);

    [RelayCommand]
    private Task CloseOtherTabsAsync(PaneViewModel? tab)
        => tab is null ? Task.CompletedTask : CloseOthersAsync(tab);

    [RelayCommand]
    private Task CloseTabsToTheRightAsync(PaneViewModel? tab)
        => tab is null ? Task.CompletedTask : CloseToTheRightAsync(tab);

    /// <summary>마지막 탭과 고정 탭은 닫히지 않는다 (docs/PRD-v2.md §17).</summary>
    private bool CanClose(PaneViewModel tab) => tabs.Count > 1 && !tab.IsPinned;

    /// <summary>
    /// 떠나는 탭의 감시를 놓고, 오는 탭을 연다.
    /// </summary>
    private async Task SwitchAsync(PaneViewModel leaving, PaneViewModel arriving, CancellationToken ct)
    {
        // <b>놓기는 취소하지 않는다.</b> 전환이 겹쳐 이 작업이 버려져도 떠난 탭이 감시를
        // 든 채 남으면 docs/PRD-v2.md §13 의 폭주가 화면 밖에서 돈다 — 막으려던 것이 바로
        // 그것이다. 놓기 자체는 인자를 받지 않고 항상 끝난다.
        await leaving.SuspendWatchAsync().ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            // 더 새로운 전환이 있었다. 여는 것은 그 전환이 한다.
            return;
        }

        await OpenAsync(arriving, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 탭 하나를 화면에 세운다 — 아직 열지 않았으면 열고, 열었으면 새로 고친다.
    /// <para>
    /// 탭 전환(<see cref="SwitchAsync"/>)과 페인 펴기(<see cref="ResumeAsync"/>)가 이 자리를
    /// 나눠 쓴다. <b>같은 일이 두 자리에 있으면 한쪽만 고쳐진다</b> — 감시를 다시 거는 길이
    /// 갈리면 §13 의 폭주가 한쪽 경로에서만 막힌다.
    /// </para>
    /// </summary>
    private static async Task OpenAsync(PaneViewModel arriving, CancellationToken ct)
    {
        // 아직 열지 않은 탭 — 복원된 배경 탭이거나 방금 만든 탭이다. 히스토리에 기록하며
        // 연다: 그 탭에서 뒤로를 누를 자리가 생긴다.
        if (arriving.PendingLocation is { } pending)
        {
            arriving.PendingLocation = null;

            await arriving.NavigateAsync(pending, ct).ConfigureAwait(false);

            return;
        }

        // 이미 열었다. 새로 고침 한 번이 배경에서 놓친 변경을 메우고 감시를 다시 건다.
        // 아무 곳도 열지 않은 탭이면 RefreshAsync 가 그대로 돌아온다.
        await arriving.RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>탭 하나를 만들어 목록에 끼운다.</summary>
    private PaneViewModel Add(int index) => Adopt(createPane(), index);

    /// <summary>
    /// 탭을 이 페인의 것으로 만든다 — 만든 탭과 건너온 탭이 같은 길을 지난다
    /// (<see cref="Add"/> · <see cref="Receive"/>).
    /// </summary>
    private PaneViewModel Adopt(PaneViewModel tab, int index)
    {
        // 정책을 구독 <b>보다 먼저</b> 입힌다. 뒤집으면 이 대입이 컬럼 알림으로 돌아와
        // "사용자가 경계를 끌었다" 로 읽힌다 (OnTabPropertyChanged).
        //
        // 폴더보다 먼저이기도 하다 — 나중에 밀면 새 탭이 한 번은 옛 정책으로 그려지고
        // 아무도 다시 읽지 않는다 (docs/PRD-v2.md §17 구조 렌즈).
        tab.ShowHiddenItems = showHiddenItems;
        tab.Terminal = terminal;
        tab.Columns = columns;

        tab.ActivationRequested += OnTabActivationRequested;
        tab.PinRequested += OnTabPinRequested;
        tab.PropertyChanged += OnTabPropertyChanged;

        tabs.Insert(index < 0 ? 0 : index > tabs.Count ? tabs.Count : index, tab);

        InvalidateStrip();
        OnPropertyChanged(nameof(ActiveIndex));
        OnPropertyChanged(nameof(CanCloseActive));

        return tab;
    }

    /// <summary>
    /// 여러 탭을 닫는다. <b>오른쪽부터</b> 닫는 이유: 되살리기가 최근 것부터 꺼내므로
    /// 그 순서로 쌓이면 원래 자리가 그대로 복원된다.
    /// </summary>
    private async Task CloseAllAsync(IReadOnlyList<PaneViewModel> targets)
    {
        for (var index = targets.Count - 1; index >= 0; index--)
        {
            // CanClose 가 고정 탭과 마지막 탭을 거부한다 — 여기서 다시 가리지 않는다.
            await CloseAsync(targets[index]).ConfigureAwait(false);
        }
    }

    /// <summary>고정 탭이 앞에 모인 순서. 그 안에서는 목록 순서를 지킨다.</summary>
    private IReadOnlyList<PaneViewModel> BuildStripTabs()
        => [.. tabs.Where(tab => tab.IsPinned), .. tabs.Where(tab => !tab.IsPinned)];

    private void InvalidateStrip()
    {
        stripTabs = null;

        OnPropertyChanged(nameof(StripTabs));
    }

    /// <summary>구독을 끊는다. 접는 것은 부르는 쪽이 한다 — 순서가 거기서 정해진다.</summary>
    private void Abandon(PaneViewModel tab)
    {
        tab.ActivationRequested -= OnTabActivationRequested;
        tab.PinRequested -= OnTabPinRequested;
        tab.PropertyChanged -= OnTabPropertyChanged;
    }

    /// <summary>
    /// 탭 하나를 저장할 모양으로 낸다. 폴더가 없으면 <see langword="null"/> 이다 —
    /// 아직 열지 않은 배경 탭은 <see cref="PaneViewModel.PendingLocation"/> 이 폴더다.
    /// </summary>
    private static TabState? Capture(PaneViewModel tab)
        => (tab.CurrentLocation ?? tab.PendingLocation) is { } folder
            ? new TabState(folder, tab.IsPinned, tab.CustomTitle)
            : null;

    private void Step(int delta)
    {
        if (tabs.Count < 2)
        {
            return;
        }

        // 순환한다 (docs/PRD-v2.md §17) — 탭이 2~4개인 화면에서 Ctrl+Tab 으로 어디든 닿는다.
        Activate(tabs[((ActiveIndex + delta) % tabs.Count + tabs.Count) % tabs.Count]);
    }

    private void PushColumns()
    {
        pushingColumns = true;

        try
        {
            foreach (var tab in tabs)
            {
                tab.Columns = columns;
            }
        }
        finally
        {
            pushingColumns = false;
        }

        // 페인이 값을 자른다 (PaneViewModel.ClampColumn). 자른 뒤의 값을 들고 있어야
        // 같은 값을 다시 밀 때 알림이 한 번 더 나가지 않는다.
        columns = active.Columns;
    }

    private void OnTabActivationRequested(object? sender, EventArgs args)
        => ActivationRequested?.Invoke(this, args);

    private void OnTabPinRequested(object? sender, IReadOnlyList<string> paths)
        => PinRequested?.Invoke(this, paths);

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PaneViewModel.CurrentLocation))
        {
            // 배경 탭의 이동은 트리를 끌고 다니지 않는다 — 그러면 트리가 어느 쪽을
            // 가리키는지 알 수 없고, 그 탐색은 전부 저장소 호출이다.
            if (ReferenceEquals(sender, active))
            {
                ActiveLocationChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (args.PropertyName == nameof(PaneViewModel.IsPinned))
        {
            // 고정은 목록을 건드리지 않는다. 바뀌는 것은 그릴 순서와 닫힘 여부뿐이다.
            InvalidateStrip();
            OnPropertyChanged(nameof(CanCloseActive));

            return;
        }

        // 사용자가 컬럼 경계를 끌었다. 폭의 소유자는 페인이므로 그 값을 여기로 올려
        // 나머지 탭에 밀어야 한다 — 안 하면 탭 전환마다 컬럼이 튄다.
        if (!pushingColumns && sender is PaneViewModel tab && IsColumnWidth(args.PropertyName))
        {
            Columns = tab.Columns;
        }
    }

    private static bool IsColumnWidth(string? name)
        => name is nameof(PaneViewModel.NameColumnWidth)
            or nameof(PaneViewModel.SizeColumnWidth)
            or nameof(PaneViewModel.TypeColumnWidth)
            or nameof(PaneViewModel.ModifiedColumnWidth);
}
