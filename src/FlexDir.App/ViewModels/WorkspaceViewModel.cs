using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.Core.Settings;
using FlexDir.Core.ViewState;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 닫은 탭 하나 — 어느 페인의 어느 자리였는지까지 (docs/PRD-v2.md §17 되살리기).
/// 스택이 창 전체에 하나이므로 페인을 함께 들어야 원래 자리로 돌아갈 수 있다.
/// <para>
/// <b>페인을 번호가 아니라 인스턴스로 든다</b> (docs/PRD-v2.md §18). 접힌 페인도 살아 있어
/// 번호가 밀리지 않지만, 되살릴 때 그 페인이 <b>지금 보이는가</b>를 물어야 한다 — 화면 밖에서
/// 탭이 조용히 생기면 아무 일도 안 한 것처럼 보인다.
/// </para>
/// </summary>
internal readonly record struct ClosedTabRecord(PaneTabsViewModel Pane, ClosedTab Closed);

/// <summary>
/// "분할" 메뉴의 항목 하나 (docs/PRD-v2.md §18). 목록을 ViewModel 이 소유하는 이유는
/// <see cref="GroupOption"/> 과 같다 — 항목을 XAML 에 두 벌 쓰면 곧 어긋나고, 지금 몇 분할인지
/// 체크 표시가 맞는지도 자동 채점할 수 없게 된다.
/// <para>
/// <c>int</c> 를 <c>CommandParameter</c> 로 XAML 에 직접 적을 수 없다는 실무적인 이유도 있다 —
/// 거기 적은 <c>"2"</c> 는 문자열이고, <c>RelayCommand&lt;int&gt;</c> 는 그것을 받으면 던진다.
/// </para>
/// </summary>
public sealed record SplitOption(int Count, string Label, bool IsSelected);

/// <summary>
/// 창 전체. 페인들과 활성 페인, 그리고 폴더와 무관한 전역 상태를 쥔다.
/// <para>
/// 페인은 서로 완전히 독립이다 (docs/PRD.md §4) — 여기에는 한 페인의 조작을 다른 페인으로
/// 옮기는 경로가 <see cref="OpenOtherPaneLocationAsync"/> 하나뿐이고, 그것도 사용자가 명령을
/// 실행할 때만 움직인다. 공용 정렬·공용 선택 같은 상태를 두지 않는다.
/// </para>
/// <para>
/// <b>페인은 하나에서 넷까지다</b> (docs/PRD-v2.md §18 · 사용자 결정 2026-08-12). 좌·우라는
/// 이름이 여기서 사라졌다 — <see cref="Panes"/> 의 자리(번호)가 그것을 대신하고, 어느 번호가
/// 화면 어디에 앉는지는 View 가 든다 (<c>Views/SplitLayout.cs</c>). ViewModel 이 화면의
/// 기하를 알면 분할 배치를 바꿀 때마다 두 계층을 함께 고쳐야 한다.
/// </para>
/// <para>
/// 창 배치는 <see cref="WindowPlacement"/> 로 노출하는 것까지가 범위다. 실제로 창에 적용하는
/// 것은 View 의 일이다 — App 의 ViewModel 은 WPF 창을 만지지 않는다 (docs/ARCHITECTURE.md §1).
/// </para>
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    /// <summary>
    /// 왼쪽 열 비율의 하한·상한. 창 최소 너비 900 에서 페인 최소 너비 320 을 지키려면
    /// 비율이 양 끝으로 갈 수 없다 (docs/DESIGN.md §1).
    /// </summary>
    private const double MinSplitterRatio = 0.15;

    private const double MaxSplitterRatio = 0.85;

    /// <summary>
    /// 되살릴 수 있는 닫은 탭의 수 (docs/PRD-v2.md §17). 종료하면 비운다 — 프로세스
    /// 수명이라 별도로 지울 자리가 없다.
    /// </summary>
    private const int ReopenDepth = 10;

    private readonly Func<PaneViewModel> paneFactory;
    private readonly IViewStateStore viewStates;

    /// <summary>
    /// 닫은 탭. <b>창 전체에 하나다</b> (docs/PRD-v2.md §17) — 끝이 가장 최근이다.
    /// 스택 타입을 쓰지 않는 이유: 깊이를 넘길 때 <b>가장 오래된 것</b>을 버려야 하고
    /// <c>Stack&lt;T&gt;</c> 는 바닥에 닿는 길이 없다.
    /// </summary>
    private readonly List<ClosedTabRecord> closedTabs = [];

    /// <summary>
    /// <b>만든 페인 전부 — 접힌 것까지</b> (docs/PRD-v2.md §18). 앞에서부터
    /// <see cref="SplitCount"/> 개가 화면에 있다.
    /// <para>
    /// 접힌 페인이 여기 남는 것이 "다시 폄면 그대로" 의 전부다 (사용자 결정 2026-08-12).
    /// 자리(번호)가 안 밀리는 것도 여기서 나온다 — 4분할에서 1분할로 접었다가 다시 펴면
    /// 3번 페인이 3번 자리로 돌아온다.
    /// </para>
    /// </summary>
    private readonly List<PaneTabsViewModel> allPanes = [];

    /// <summary>화면에 보이는 페인. View 가 이것을 그린다.</summary>
    private readonly ObservableCollection<PaneTabsViewModel> visiblePanes = [];

    /// <summary>
    /// 활성이었던 순서 — <b>끝이 가장 최근</b>이다. '다른 페인' 의 대상이 여기서 나온다
    /// (사용자 결정 2026-08-12: MRU).
    /// <para>
    /// 2분할에서는 이것이 곧 "반대편" 이라 예전 동작과 완전히 같다. 3·4분할에서 비로소
    /// 갈리고, 그때 "방금 있던 곳" 이 왕복 작업의 실제 모양이다.
    /// </para>
    /// </summary>
    private readonly List<PaneTabsViewModel> recent = [];

    private PaneTabsViewModel activePane;
    private int splitCount = 1;

    /// <summary>"분할" 메뉴 항목. 분할 수가 바뀌면 버린다 (<see cref="SplitOptions"/>).</summary>
    private IReadOnlyList<SplitOption>? splitOptions;

    private double splitterRatio = GlobalViewState.Default.SplitterRatio;
    private double rowRatio = GlobalViewState.Default.RowRatio;
    private WindowPlacement? windowPlacement;

    /// <summary>숨김 정책이 바뀌어 다시 읽는 중인 작업. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    private Task hiddenItemsWork = Task.CompletedTask;

    /// <summary>진행 중인 트리 따라가기. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    private Task treeRevealWork = Task.CompletedTask;

    /// <summary>진행 중인 분할 변경 — 접기의 감시 해제와 펴기의 열기. 테스트가 보는 자리다.</summary>
    private Task splitWork = Task.CompletedTask;

    /// <param name="update">
    /// 새 버전 알림 (docs/PRD-v2.md §9). <b>선택이다</b> — 없으면 알림 바가 영영 접혀 있고
    /// 나머지는 그대로 돈다. 창은 업데이트 없이도 서야 한다: 조립이 그 자리에서 막히면
    /// 업데이트와 무관한 앱 전체가 못 뜬다.
    /// </param>
    /// <param name="tree">
    /// 왼쪽 폴더 트리 (docs/PRD-v2.md §10). <paramref name="update"/> 와 같은 이유로
    /// 선택이다 — 페인은 트리 없이도 돌아야 하고, 트리를 보지 않는 테스트가 드라이브
    /// 열거 fake 까지 조립하게 만들 이유가 없다.
    /// </param>
    /// <param name="settings">
    /// 설정 패널 (docs/PRD-v2.md §12). 앞의 둘과 같은 이유로 선택이다 — 없으면 시작 폴더는
    /// 마지막 폴더로, 숨김 정책은 기본값으로 간다.
    /// </param>
    /// <param name="paneFactory">
    /// 탭 하나를 만드는 법 (docs/PRD-v2.md §17). <b>페인 인스턴스가 아니라 팩토리를 받는다</b> —
    /// 탭도 <b>페인도</b> 런타임에 늘어나므로 조립 시점에 인스턴스를 다 알 수 없다. 조립은 이미
    /// 열려 있었다: <c>AppComposition.Create</c> 의 <c>Pane()</c> 지역 함수가 그대로 이것이다.
    /// </param>
    public WorkspaceViewModel(
        Func<PaneViewModel> paneFactory,
        IViewStateStore viewStateStore,
        UpdateViewModel? update = null,
        FolderTreeViewModel? tree = null,
        SettingsViewModel? settings = null)
    {
        ArgumentNullException.ThrowIfNull(paneFactory);
        ArgumentNullException.ThrowIfNull(viewStateStore);

        this.paneFactory = paneFactory;
        Update = update;
        Tree = tree;
        Settings = settings;
        viewStates = viewStateStore;

        Panes = new ReadOnlyObservableCollection<PaneTabsViewModel>(visiblePanes);

        // 1분할이 기본화면이다 (사용자 결정 2026-08-12). 페인 하나가 조립 직후부터 서 있고,
        // 그래서 "페인 0개" 는 어느 시점에도 존재하지 않는다 — 탭이 항상 1개 이상인 것과
        // 같은 수다 (PaneTabsViewModel 의 생성자).
        //
        // Show 가 아니라 Reveal 인 것은 여는 일을 생성자에서 하지 않기 위해서다 — 폴더를
        // 여는 것은 RestoreAsync 의 몫이고, 조립은 저장소에 닿지 않는다 (CLAUDE.md §3).
        activePane = Grow(1);

        Reveal(1);

        // 0번은 처음부터 활성이다 — Grow 가 MRU 맨 앞에 넣어 둔 것을 맨 뒤로 올린다.
        Touch(activePane);

        if (settings is not null)
        {
            // 상태 폴더 열기. 트리의 NavigationRequested 와 같은 자리다 — 설정은 페인을
            // 모르고, 어느 페인이 갈지는 여기서 정한다.
            settings.NavigationRequested += (_, location) => _ = ActiveTab.NavigateAsync(location);

            // 숨김 정책이 바뀌면 지금 보고 있는 것을 다시 걸러야 한다. 페인과 트리가
            // 스스로 하지 않는 이유는 각자의 속성 주석에 있다 — 다시 읽을지를 정하는
            // 곳이 하나여야 클릭 한 번에 저장소 호출이 셋으로 늘지 않는다.
            settings.HiddenItemsChanged += (_, _) => hiddenItemsWork = ApplyHiddenItemsAsync();
        }

        if (tree is not null)
        {
            // 어느 페인이 갈지는 트리가 아니라 여기서 정한다 (사용자 결정 2026-08-10:
            // 활성 페인). 트리가 페인을 직접 알면 창 하나에 트리 하나라는 전제가 트리
            // 안으로 새어 든다.
            //
            // <b>페인마다 거는 배선(<see cref="Wire"/>)과 갈리는 자리다</b> — 트리는 창에
            // 하나뿐이라 페인이 늘어도 이 구독은 하나여야 한다. 반대 방향(페인이 옮기면
            // 트리가 따라간다)이 페인마다인 것과 짝이다.
            //
            // 기다리지 않는다 — 이벤트 핸들러는 동기이고, 페인은 열거 실패를 자기
            // 상태표시줄에 낸다 (PaneViewModel.FillAsync).
            tree.NavigationRequested += (_, location) => _ = ActiveTab.NavigateAsync(location);
        }
    }

    /// <summary>
    /// 화면에 보이는 페인들 (docs/PRD-v2.md §18). 순서가 곧 슬롯 번호이고, 어느 슬롯이 어느
    /// 칸에 앉는지는 View 가 정한다 (<c>Views/SplitLayout.cs</c>).
    /// <para>
    /// 읽기 전용으로 낸다 — 넣고 빼는 자리가 <see cref="SetSplit"/> 하나여야 접기의 감시
    /// 해제와 MRU 가 함께 따라간다.
    /// </para>
    /// </summary>
    public ReadOnlyObservableCollection<PaneTabsViewModel> Panes { get; }

    /// <summary>
    /// 슬롯 0~3 에 앉은 페인. 비어 있으면 <see langword="null"/> 이고 View 가 그 칸을 접는다
    /// (<c>Views/SplitLayout.cs</c>).
    /// <para>
    /// <b>넷을 따로 내는 이유는 XAML 이다.</b> <c>{Binding Panes[3]}</c> 로 물면 페인이 셋일 때
    /// 바인딩이 <b>조용히</b> 실패한다 — 컴파일러가 못 잡고 화면에서만 드러나는 자리이고,
    /// 이 저장소가 §17 에서 값을 치르고 배운 것이 정확히 그것이다 (docs/PRD-v2.md §17
    /// §값을 치르고 배운 것). 속성이면 <c>null</c> 이 정상 값이라 실패할 자리가 없다.
    /// </para>
    /// </summary>
    public PaneTabsViewModel? Pane0 => Slot(0);

    /// <inheritdoc cref="Pane0"/>
    public PaneTabsViewModel? Pane1 => Slot(1);

    /// <inheritdoc cref="Pane0"/>
    public PaneTabsViewModel? Pane2 => Slot(2);

    /// <inheritdoc cref="Pane0"/>
    public PaneTabsViewModel? Pane3 => Slot(3);

    /// <summary>
    /// 새 버전 알림. 폴더와 무관한 전역 상태라 여기 산다 — 페인이 여럿인데 알림은 하나다.
    /// 배선되지 않았으면 <see langword="null"/> 이고 알림 바는 접혀 있다.
    /// </summary>
    public UpdateViewModel? Update { get; }

    /// <summary>
    /// 왼쪽 폴더 트리. 페인이 여럿인데 트리는 하나다 — 알림과 같은 자리에 산다.
    /// 배선되지 않았으면 <see langword="null"/> 이고 View 가 그 열을 접는다.
    /// </summary>
    public FolderTreeViewModel? Tree { get; }

    /// <summary>
    /// 설정 패널 (docs/PRD-v2.md §12). 알림·트리와 같은 자리에 산다 — 페인이 여럿인데
    /// 설정은 하나다. 배선되지 않았으면 <see langword="null"/> 이고 오버레이가 뜨지 않는다.
    /// </summary>
    public SettingsViewModel? Settings { get; }

    /// <summary>
    /// UI 스레드 사고 알림 (docs/PRD-v2.md §14). 페인이 여럿인데 알림은 하나라 위의 셋과
    /// 같은 자리에 산다.
    /// <para>
    /// <b>다만 이것만 선택이 아니다.</b> 포트를 들지 않아 조립이 실패할 자리가 없고,
    /// 예외를 살아남는 것은 어느 조립에서도 꺼져 있으면 안 된다 — <c>Host/Program</c> 이
    /// 여기에 <c>DispatcherUnhandledException</c> 을 건다.
    /// </para>
    /// </summary>
    public CrashNoticeViewModel Crash { get; } = new();

    /// <summary>숨김 정책 변경에 이어지는 재열거. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    internal Task HiddenItemsWork => hiddenItemsWork;

    /// <summary>진행 중인 트리 따라가기. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    internal Task TreeRevealWork => treeRevealWork;

    /// <summary>진행 중인 분할 변경. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    internal Task SplitWork => splitWork;

    /// <summary>
    /// 접힌 것까지 포함한 페인 전부 (docs/PRD-v2.md §18). <b><c>Host</c> 가 종료할 때 이것을
    /// 지난다</b> — 화면에 없다고 인스턴스가 없는 것이 아니라, <see cref="Panes"/> 만 접으면
    /// 접힌 페인의 썸네일 스케줄러와 열거 세션이 shell 구현체가 닫힌 뒤에도 남는다.
    /// </summary>
    public IReadOnlyList<PaneTabsViewModel> AllPanes => allPanes;

    /// <summary>
    /// 화면에 보이는 페인 수 (1~<see cref="GlobalViewState.MaxPanes"/>). 배치는 프리셋이라
    /// 이 수 하나로 정해진다 (사용자 결정 2026-08-12).
    /// </summary>
    public int SplitCount => splitCount;

    /// <summary>
    /// 페인을 닫을 수 있는가 — 1분할에서는 닫을 페인이 없다. View 가 '이 페인 닫기' 를
    /// 감추는 근거다 (탭의 <c>CanCloseActive</c> 와 같은 자리).
    /// </summary>
    public bool CanClosePane => splitCount > 1;

    /// <summary>분할을 더 늘릴 수 있는가. View 가 '여기서 분할' 을 감추는 근거다.</summary>
    public bool CanSplit => splitCount < GlobalViewState.MaxPanes;

    /// <summary>
    /// "분할" 메뉴에 올릴 넷. 분할이 바뀌면 통째로 새로 만든다 — 인스턴스가 그대로면
    /// View 가 체크 표시를 다시 그릴 신호를 못 받는다 (<c>PaneViewModel.GroupOptions</c> 와
    /// 같은 수).
    /// </summary>
    public IReadOnlyList<SplitOption> SplitOptions => splitOptions ??= BuildSplitOptions();

    /// <summary>활성 페인은 항상 정확히 하나다. 보이는 페인 중 하나임이 불변식이다.</summary>
    public PaneTabsViewModel ActivePane
    {
        get => activePane;
        private set
        {
            if (ReferenceEquals(activePane, value))
            {
                // 값이 같아도 MRU 는 갱신한다 — 같은 페인을 다시 눌렀다고 순서가 흔들리지는
                // 않지만, 아래 Touch 는 멱등이라 여기서 갈라 둘 이유가 없다.
                return;
            }

            activePane = value;

            Touch(value);

            OnPropertyChanged();

            // 파생 속성이라 값 비교로 걸러낼 수 없다. View 가 활성 표시를 여기에
            // 바인딩한다 (docs/DESIGN.md §6).
            OnPropertyChanged(nameof(ActiveTab));
            OnPropertyChanged(nameof(OtherPane));
            OnPropertyChanged(nameof(OtherTab));

            // 가리키는 페인이 바뀌었으니 트리도 옮겨간다 — Tab 으로 옮긴 뒤에도 트리가
            // 다른 페인의 자리를 가리키면 어느 쪽을 보고 있는지 알 수 없다.
            RevealInTree();
        }
    }

    /// <summary>
    /// 활성 페인의 <b>활성 탭</b>. 키보드 조작이 향하는 곳이다 — 탭이 들어오며 한 단
    /// 깊어진 자리가 여기다 (docs/PRD-v2.md §17 · ADR-018).
    /// </summary>
    public PaneViewModel ActiveTab => activePane.Active;

    /// <summary>
    /// '다른 페인' — <b>직전에 활성이었던, 지금 보이는 페인</b> (사용자 결정 2026-08-12: MRU).
    /// 페인 간 복사·이동·보내기의 대상이다.
    /// <para>
    /// <b>1분할에서는 <see langword="null"/> 이다.</b> 갈 곳이 없다는 것을 타입으로 말한다 —
    /// 자기 자신을 내면 '반대편으로 복사' 가 제자리 복사가 되고, 그것은 조용히 파일을 부르는
    /// 일이다.
    /// </para>
    /// </summary>
    public PaneTabsViewModel? OtherPane
    {
        get
        {
            // 끝에서부터 본다 — 가장 최근이 끝이다. 접힌 페인은 건너뛴다: 화면에 없는
            // 페인으로 복사하면 어디로 갔는지 볼 수 없다.
            for (var index = recent.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(recent[index], activePane) && visiblePanes.Contains(recent[index]))
                {
                    return recent[index];
                }
            }

            return null;
        }
    }

    /// <summary>'다른 페인' 의 활성 탭. 페인 간 복사·이동의 대상이다.</summary>
    public PaneViewModel? OtherTab => OtherPane?.Active;

    /// <summary>
    /// 왼쪽 열이 차지하는 비율. 범위를 벗어난 값은 예외 없이 가장 가까운 경계로 잘린다 —
    /// 스플리터를 끝까지 끌거나 저장 파일이 손상돼도 페인이 사라지면 안 된다.
    /// </summary>
    public double SplitterRatio
    {
        get => splitterRatio;

        // 자르고 나서 비교한다. 경계를 넘어 계속 끌어도 알림은 한 번이다.
        set => SetProperty(ref splitterRatio, Clamp(value));
    }

    /// <summary>
    /// 위 행이 차지하는 비율 (docs/PRD-v2.md §18). <b>격자 전체에 하나다</b> — 4분할에서
    /// 좌·우가 이 값을 나눠 써야 가로줄이 일직선으로 이어진다.
    /// </summary>
    public double RowRatio
    {
        get => rowRatio;
        set => SetProperty(ref rowRatio, Clamp(value));
    }

    /// <summary>복원·저장 대상 창 배치. 기억된 것이 없으면 null 이다.</summary>
    public WindowPlacement? WindowPlacement
    {
        get => windowPlacement;
        set => SetProperty(ref windowPlacement, value);
    }

    /// <summary>
    /// 저장된 전역 상태(분할 모양·비율·창 배치·마지막 폴더)를 복원한다.
    /// <para>
    /// 페인은 마지막 폴더로, 기억이 없으면 <paramref name="fallbackFolder"/> 로 간다 —
    /// 빈 페인으로 시작하면 매번 주소를 쳐야 한다. 폴백마저 없으면 비워 둔다.
    /// 사라진 폴더는 페인이 알아서 상위로 올라간다 (docs/PRD.md §4).
    /// </para>
    /// </summary>
    public async Task RestoreAsync(Core.Locations.LocationId? fallbackFolder, CancellationToken ct = default)
    {
        var state = await LoadGlobalAsync(ct).ConfigureAwait(false);

        // 설정을 먼저 읽는다. 시작 폴더 규칙과 숨김 정책이 둘 다 여기서 나오고, 둘 다
        // 폴더를 열기 <b>전에</b> 서 있어야 한다 — 나중에 밀면 시작할 때 한 번은 옛 정책으로
        // 그려지고 아무도 다시 읽지 않는다.
        var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);

        var remembered = state.Panes ?? [];

        // 기억된 페인과 보일 페인 중 많은 쪽까지 만든다. 접힌 페인도 인스턴스가 있어야
        // 다시 폈을 때 그대로 돌아온다 (사용자 결정 2026-08-12).
        Grow(Math.Max(remembered.Count, state.PaneCount));

        // 클램프를 지난다 — 저장된 값이 0.02 여도 페인 하나가 사라지지 않는다.
        SplitterRatio = state.SplitterRatio;
        RowRatio = state.RowRatio;
        WindowPlacement = state.Window;

        // 여는 것은 아래 루프가 한다 — 여기서 펴면 같은 폴더를 두 번 열거한다.
        Reveal(state.PaneCount);

        OnPropertyChanged(nameof(SplitCount));
        OnPropertyChanged(nameof(CanClosePane));
        OnPropertyChanged(nameof(CanSplit));

        var opens = new List<Task>(allPanes.Count + 1);

        if (Tree is { } tree)
        {
            // 폭도 클램프를 지난다 (FolderTreeViewModel.Width) — 손상된 값에 트리가
            // 사라지지 않는다.
            tree.IsVisible = state.TreeVisible;
            tree.Width = state.TreeWidth;
            tree.ShowHiddenItems = settings.ShowHiddenItems;

            // 드라이브 열거는 저장소에 닿는다. 페인 열기와 함께 두는 이유도 같다 —
            // 트리 하나 때문에 폴더가 늦게 뜨면 안 된다.
            opens.Add(tree.LoadAsync(ct));
        }

        for (var index = 0; index < allPanes.Count; index++)
        {
            var pane = allPanes[index];
            var tabs = index < remembered.Count ? remembered[index].Tabs : null;

            // 컬럼 폭은 페인마다 따로다 (사용자 지적 2026-08-10) — 한 페인에서 끌 때 다른
            // 페인이 함께 움직이면 안 된다. 한 페인 <b>안의</b> 탭들은 이것을 나눠 쓴다.
            pane.Columns = index < remembered.Count ? remembered[index].Columns : PaneColumns.Default;

            // 소유자가 페인이므로 복원된 탭과 앞으로 만들 탭이 모두 이 정책으로 뜬다
            // (docs/PRD-v2.md §17 구조 렌즈 — 예전에는 나중에 만든 탭이 기본값으로 떴다).
            pane.ShowHiddenItems = settings.ShowHiddenItems;

            // 시작 폴더 규칙은 Core 에 있다 (AppSettings.ResolveStartFolder) — 페인마다 한 번씩
            // 같은 규칙을 지난다. 규칙이 정하는 것은 <b>활성 탭</b>이 열 폴더이고, 배경 탭은
            // 기억된 자기 폴더를 그대로 든다 (docs/PRD-v2.md §17 세션 복원).
            opens.Add(pane.RestoreAsync(
                tabs,
                settings.ResolveStartFolder(ActiveFolder(tabs), fallbackFolder),

                // 접힌 채로 복원되는 페인은 열지 않는다 — 화면에 없는 페인이 감시를 들면
                // §13 의 폭주가 보이지 않는 자리에서 돈다.
                open: index < state.PaneCount,
                ct));
        }

        // 함께 연다 — 페인은 독립이라 하나가 느려도 (네트워크·대용량) 다른 것을 막지 않는다.
        await Task.WhenAll(opens).ConfigureAwait(false);
    }

    /// <summary>현재 전역 상태를 저장한다. 마지막 폴더가 다음 실행의 시작 폴더다.</summary>
    public async Task PersistAsync(CancellationToken ct = default)
    {
        try
        {
            // 접힌 페인도 담는다 (docs/PRD-v2.md §18) — 그러지 않으면 접기가 재시작을 건너며
            // 닫기가 된다. 담을 탭이 하나도 없는 페인은 빠지고, 그러면 뒤의 페인이 앞으로
            // 당겨진다: 자리를 비워 둘 길이 저장 포맷에 없다.
            var panes = allPanes
                .Select(pane => pane.Capture() is { } tabs ? new PaneState(tabs, pane.Columns) : null)
                .OfType<PaneState>()
                .ToArray();

            await viewStates
                .SaveGlobalAsync(
                    new GlobalViewState(
                        SplitterRatio,
                        WindowPlacement,
                        panes.Length == 0 ? null : panes,
                        splitCount,
                        RowRatio,
                        Tree?.IsVisible ?? true,
                        Tree?.Width ?? GlobalViewState.DefaultTreeWidth),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 읽기와 같은 이유로 삼킨다 — 저장 실패가 창을 닫는 길을 막으면 안 된다.
        }
    }

    // ── 분할 (docs/PRD-v2.md §18) ───────────────────────────────────
    //
    // 배치는 프리셋 네 단계다 (사용자 결정 2026-08-12). 여기서 정하는 것은 <b>몇 개인가</b>
    // 뿐이고, 어느 슬롯이 화면 어디에 앉는지는 View 가 든다 — 그 표가 두 계층으로 갈리면
    // 배치를 바꿀 때마다 둘 다 고쳐야 한다 (ViewMode → 아이콘 크기와 같은 판단).

    /// <summary>
    /// 분할 수를 정한다 (툴바 버튼 · 탭 줄 우클릭 · docs/PRD-v2.md §18).
    /// <para>
    /// <b>줄이는 것은 접는 것이지 닫는 것이 아니다</b> (사용자 결정 2026-08-12) — 인스턴스는
    /// 남고 감시만 놓는다. 늘리면 그 자리에 있던 페인이 자기 폴더를 들고 그대로 돌아오고,
    /// 한 번도 없던 자리만 활성 페인을 복제한다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SetSplit(int count)
    {
        var next = count < 1 ? 1
            : count > GlobalViewState.MaxPanes ? GlobalViewState.MaxPanes
            : count;

        if (next == splitCount)
        {
            return;
        }

        Grow(next);

        splitWork = Show(next);

        OnPropertyChanged(nameof(SplitCount));
        OnPropertyChanged(nameof(CanClosePane));
        OnPropertyChanged(nameof(CanSplit));

        // 활성 페인이 접혔으면 보이는 페인 중 가장 최근으로 옮긴다. 활성 표시가 화면 밖에
        // 있으면 키보드가 어디로 가는지 볼 수 없다.
        if (!visiblePanes.Contains(activePane))
        {
            ActivePane = MostRecentVisible();
        }

        // 접히고 펴진 것 때문에 '다른 페인' 이 바뀔 수 있다 — 활성 페인이 그대로여도 그렇다.
        OnPropertyChanged(nameof(OtherPane));
        OnPropertyChanged(nameof(OtherTab));
    }

    /// <summary>
    /// 분할을 하나 늘린다 (탭 줄 우클릭 '여기서 분할'). 넷이면 아무 일도 하지 않는다 —
    /// 그 판정은 <see cref="SetSplit"/> 이 한다.
    /// </summary>
    [RelayCommand]
    private void SplitOnce() => SetSplit(splitCount + 1);

    /// <summary>
    /// 이 페인을 닫는다 — 실제로는 <b>맨 뒤를 접는다</b> (docs/PRD-v2.md §18).
    /// <para>
    /// <b>가운데 것을 골라 지울 수 없다.</b> 프리셋 배치에서 슬롯은 앞에서부터 차므로 3번을
    /// 지우면 4번이 3번 자리로 당겨지고, 그러면 "다시 폄면 그대로" 가 깨진다 — 접었다 편
    /// 사람이 다른 폴더를 보게 된다. 대신 닫으라고 지목한 페인이 <b>활성이 아니었다면</b>
    /// 그것을 맨 뒤와 맞바꾸고 접는다: 화면에서 사라지는 것은 지목한 그 페인이다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ClosePane(PaneTabsViewModel? pane)
    {
        if (splitCount <= 1)
        {
            return;
        }

        var target = pane ?? activePane;
        var index = visiblePanes.IndexOf(target);

        if (index < 0)
        {
            return;
        }

        // 지목한 것이 맨 뒤가 아니면 맨 뒤와 맞바꾼다. allPanes 의 자리도 함께 바꿔야
        // 저장·복원이 같은 것을 본다.
        var last = splitCount - 1;

        if (index != last)
        {
            (allPanes[index], allPanes[last]) = (allPanes[last], allPanes[index]);
            (visiblePanes[index], visiblePanes[last]) = (visiblePanes[last], visiblePanes[index]);
        }

        SetSplit(splitCount - 1);
    }

    /// <summary>
    /// 활성 페인을 지정한다. 선택은 어느 페인도 건드리지 않는다 — 비활성 페인의 선택은 그대로
    /// 남고 표시만 강등된다(<c>--sel-inactive</c>). 그것은 View 의 일이다.
    /// </summary>
    [RelayCommand]
    private void Activate(PaneTabsViewModel? pane)
    {
        if (pane is not null && visiblePanes.Contains(pane))
        {
            ActivePane = pane;
        }
    }

    /// <summary>
    /// 키보드 페인 전환 (<c>Tab</c>·<c>F6</c>). <b>화면 순서로 다음 페인</b>이고 끝에서
    /// 돌아온다 — MRU 가 아니다 (사용자 결정 2026-08-12 는 '다른 페인' 의 <b>대상</b>을 정한
    /// 것이고, 순회는 예측 가능해야 한다: 같은 키를 네 번 누르면 제자리로 와야 4분할에서
    /// 길을 잃지 않는다).
    /// <para>
    /// 1분할이면 아무 일도 하지 않는다 — 갈 페인이 없다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SwitchPane()
    {
        if (splitCount <= 1)
        {
            return;
        }

        ActivePane = visiblePanes[(visiblePanes.IndexOf(activePane) + 1) % visiblePanes.Count];
    }

    // ── 탭 (docs/PRD-v2.md §17) ───────────────────────────────────
    //
    // 키보드는 활성 페인으로 간다 (Tab·F6 은 페인 전환 그대로다). 판단은 전부
    // PaneTabsViewModel 안에 있고 여기서 정하는 것은 <b>어느 페인</b>인가 뿐이다 — 페인 간
    // 복사가 "어디로" 만 정하는 것과 같은 구도다.

    /// <summary>
    /// 새 탭 (<c>Ctrl+T</c>). 활성 페인에, 지금 폴더를 복제해서 활성 탭 바로 오른쪽에.
    /// </summary>
    [RelayCommand]
    private void NewTab() => activePane.NewTab();

    /// <summary>
    /// 활성 탭 닫기 (<c>Ctrl+W</c>). 탭이 1개거나 고정 탭이면 무시된다 — 그 판정은 페인이
    /// 하고, 되살리기 스택은 <c>TabClosed</c> 를 타고 저절로 채워진다 (위 조립 참조).
    /// </summary>
    [RelayCommand]
    private Task CloseTabAsync() => activePane.CloseAsync(ActiveTab);

    /// <summary>
    /// 활성 탭을 다른 페인으로 보낸다 (컨텍스트 메뉴 '다른 페인으로 보내기' ·
    /// docs/PRD-v2.md §17). <b>살아 있는 인스턴스의 소유권이 페인을 건너간다</b> — 감시
    /// 구독·썸네일 스케줄러·열거 세션·히스토리·선택이 전부 따라온다.
    /// <para>
    /// <b>활성 페인은 따라가지 않는다</b> (사용자 결정 2026-08-11). 보내는 것은 정리
    /// 동작이라 하던 일이 있는 페인에 남는 편이 연속으로 정리하기 쉽다 — 되살리기가 페인을
    /// 옮기는 것과 갈리는 지점이고, 그쪽은 "방금 그것을 원해서 누른 키" 다.
    /// </para>
    /// <para>
    /// 마지막 탭은 보낼 수 없다 — 그러면 그 페인이 탭 0개가 된다. 그 판정은 페인이 한다.
    /// 1분할이면 보낼 곳이 없어 아무 일도 하지 않는다.
    /// </para>
    /// </summary>
    /// <param name="tab">
    /// 보낼 탭. <see langword="null"/> 이면 활성 탭이다 — <b>컨텍스트 메뉴는 우클릭한 탭을
    /// 싣는다.</b> 활성 탭으로만 받으면 활성이 아닌 탭을 우클릭해 보냈을 때 엉뚱한 것이
    /// 건너간다 (우클릭은 탭을 활성으로 만들지 않는다 — 브라우저·탐색기와 같다).
    /// </param>
    [RelayCommand]
    private Task SendTabToOtherPaneAsync(PaneViewModel? tab)
    {
        var target = tab ?? ActiveTab;

        // 어느 페인의 것인가는 목록이 안다. 떼기와 받기의 짝은 페인이 쥔다 — 탭 드래그가
        // 같은 짝을 쓴다 (PaneTabsViewModel.SendAsync).
        var source = allPanes.Find(pane => pane.Tabs.Contains(target));

        if (source is null)
        {
            return Task.CompletedTask;
        }

        // 보내는 페인이 활성이 아닐 수 있다 (우클릭). 그때의 '다른 페인' 은 활성 페인이다 —
        // 보이는 페인 중 하나여야 하고, 자기 자신이면 안 된다.
        var destination = ReferenceEquals(source, activePane) ? OtherPane : activePane;

        return destination is null ? Task.CompletedTask : source.SendAsync(target, destination);
    }

    /// <summary>다음 탭 (<c>Ctrl+Tab</c> · <c>Ctrl+PageDown</c>). 활성 페인 안에서 순환한다.</summary>
    [RelayCommand]
    private void NextTab() => activePane.Next();

    /// <summary>이전 탭 (<c>Ctrl+Shift+Tab</c> · <c>Ctrl+PageUp</c>).</summary>
    [RelayCommand]
    private void PreviousTab() => activePane.Previous();

    /// <summary>
    /// 닫은 탭 되살리기 (<c>Ctrl+Shift+T</c>). <b>창 전체에 스택 하나</b>다
    /// (docs/PRD-v2.md §17) — 실수로 닫은 것이 어느 페인이었는지 기억하지 않아도 된다.
    /// 페인별 스택이면 닫은 직후 다른 페인으로 옮겨가 누르면 돌아오지 않는다.
    /// <para>
    /// 되살린 탭이 활성이 되고 <b>그 페인도 활성이 된다</b> — 방금 그것을 원해서 누른
    /// 키다 (사용자 렌즈가 잡은 것). 그러지 않으면 화면 밖에서 조용히 생긴다.
    /// </para>
    /// <para>
    /// <b>원래 페인이 접혀 있으면 활성 페인으로 온다</b> (docs/PRD-v2.md §18). 펴 주지 않는
    /// 이유는 같은 문장이다 — 화면 밖에서 조용히 생기면 안 되고, 되살리기 키 하나가 사용자의
    /// 분할 배치를 바꾸는 것은 더 놀랍다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ReopenClosedTab()
    {
        if (closedTabs.Count == 0)
        {
            return;
        }

        var last = closedTabs[^1];
        closedTabs.RemoveAt(closedTabs.Count - 1);

        var pane = visiblePanes.Contains(last.Pane) ? last.Pane : activePane;

        pane.Insert(last.Closed.Index, last.Closed.State);

        ActivePane = pane;
    }

    /// <summary>
    /// 닫은 탭을 스택에 쌓는다. 깊이를 넘으면 <b>가장 오래된 것</b>을 버린다 —
    /// 되살리기는 최근 것부터 꺼낸다.
    /// </summary>
    private void Remember(PaneTabsViewModel pane, ClosedTab closed)
    {
        closedTabs.Add(new ClosedTabRecord(pane, closed));

        if (closedTabs.Count > ReopenDepth)
        {
            closedTabs.RemoveAt(0);
        }
    }

    /// <summary>
    /// 마우스 보조 버튼의 뒤로 (docs/PRD-v2.md §15 · 사용자 결정 2026-08-11).
    /// <para>
    /// <b>키보드와 대상 규칙이 다르다.</b> <c>Alt+←</c> 는 활성 페인 하나로 가지만
    /// (docs/DESIGN.md §9) 마우스에는 <b>자리가 있다</b> — 누른 자리의 페인이 움직이고
    /// 그 페인이 활성이 된다. 항목 클릭과 같은 규칙이다
    /// (<see cref="PaneViewModel.ActivationRequested"/>).
    /// </para>
    /// </summary>
    public Task GoBackAtAsync(PaneViewModel? under, CancellationToken ct = default)
        => Target(under).GoBackAsync(ct);

    /// <inheritdoc cref="GoBackAtAsync"/>
    public Task GoForwardAtAsync(PaneViewModel? under, CancellationToken ct = default)
        => Target(under).GoForwardAsync(ct);

    /// <summary>
    /// 마우스 조작이 향하는 페인. 그 페인을 활성으로 만들고 돌려준다.
    /// <para>
    /// 페인 밖(트리·툴바·상태표시줄)에서 누른 것은 <c>null</c> 로 오고 <b>활성 페인</b>으로
    /// 간다 — 창 안에서 누른 버튼이 아무 일도 하지 않으면 고장으로 보인다.
    /// </para>
    /// <para>
    /// 갈 곳이 없는 페인도 활성이 된다. 겨냥한 쪽이 화면에 보이는 편이 낫다 — 아무 반응이
    /// 없으면 버튼이 죽은 것과 구분되지 않는다.
    /// </para>
    /// </summary>
    private PaneViewModel Target(PaneViewModel? under)
    {
        // 어느 페인의 탭인지로 가른다. 화면에 보이는 것은 활성 탭뿐이므로 실물에서는 그것이
        // 오지만, 목록에서 찾는 편이 배경 탭이 오는 경우에도 옳은 페인을 고른다.
        if (under is not null && visiblePanes.FirstOrDefault(pane => pane.Tabs.Contains(under)) is { } owner)
        {
            ActivePane = owner;

            return owner.Active;
        }

        return ActiveTab;
    }

    /// <summary>
    /// 트리를 접거나 편다. 좁은 화면에서 가로 공간을 되찾는 길이고, 접어 둔 것은 기억된다
    /// (<see cref="PersistAsync"/>). 트리가 없으면 아무 일도 하지 않는다 — 툴바 버튼은
    /// 트리 없이 조립돼도 눌린다.
    /// </summary>
    [RelayCommand]
    private void ToggleTree()
    {
        if (Tree is { } tree)
        {
            tree.IsVisible = !tree.IsVisible;
        }
    }

    /// <summary>
    /// 설정 패널을 연다. 패널이 배선되지 않았으면 아무 일도 하지 않는다 — 타이틀바 버튼은
    /// 그런 조립에서도 눌린다 (<see cref="ToggleTree"/> 와 같은 자리).
    /// </summary>
    [RelayCommand]
    private void OpenSettings() => Settings?.OpenCommand.Execute(null);

    /// <summary>
    /// 활성 페인이 보고 있는 폴더를 시작 폴더로 삼는다 (docs/PRD-v2.md §12).
    /// <para>
    /// <b>설정은 페인을 모른다.</b> 어느 폴더인지를 아는 곳이 여기뿐이라 여기서 건넨다 —
    /// 즐겨찾기 고정(<see cref="PinCurrentFolderAsync"/>)과 같은 구도다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void UseCurrentFolderAsStart() => Settings?.UseCurrentFolder(ActiveTab.CurrentLocation);

    /// <summary>
    /// 활성 페인이 보고 있는 폴더를 트리에 고정한다 (docs/PRD-v2.md §10-2).
    /// 아무 곳도 열지 않았거나 트리가 없으면 아무 일도 하지 않는다 — 툴바 버튼은 그런
    /// 상태에서도 눌린다.
    /// </summary>
    [RelayCommand]
    private Task PinCurrentFolderAsync(CancellationToken ct)
    {
        return Tree is { } tree && ActiveTab.CurrentLocation is { } folder
            ? tree.AddFavoriteAsync(folder, ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 다른 페인의 현재 폴더를 활성 페인에서 연다 — 분할의 존재 이유가 이 왕복이다.
    /// 히스토리에 기록하므로 뒤로가 원래 폴더로 돌아간다.
    /// </summary>
    [RelayCommand]
    private Task OpenOtherPaneLocationAsync()
    {
        // 1분할이거나 다른 페인이 아직 아무 곳도 열지 않았으면 옮길 폴더가 없다.
        return OtherTab?.CurrentLocation is { } location
            ? ActiveTab.NavigateAsync(location)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 활성 페인의 선택을 다른 페인의 현재 폴더로 복사한다 — 분할이 있는 이유다
    /// (docs/PRD.md §2).
    /// <para>
    /// 조작 자체는 페인이 한다. 여기서 정하는 것은 <b>어디로</b> 보내는가 뿐이다 —
    /// 워크스페이스가 파일 조작 포트를 직접 들면 실패 사유를 어느 페인의 상태표시줄에
    /// 올려야 할지 알 수 없다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task CopyToOtherPaneAsync(CancellationToken ct)
    {
        return OtherTab?.CurrentLocation is { } destination
            ? ActiveTab.CopySelectionToAsync(destination, ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 활성 페인의 선택을 다른 페인의 현재 폴더로 이동한다. 양쪽이 같은 폴더면
    /// 아무 일도 하지 않는다 (제자리 이동).
    /// </summary>
    [RelayCommand]
    private Task MoveToOtherPaneAsync(CancellationToken ct)
    {
        return OtherTab?.CurrentLocation is { } destination
            ? ActiveTab.MoveSelectionToAsync(destination, ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 페인을 <paramref name="count"/> 개가 될 때까지 만든다. 이미 그만큼 있으면 아무 일도
    /// 하지 않는다 — <b>접힌 페인이 곧 그 "이미 있는" 것</b>이다.
    /// <para>
    /// 새로 만드는 페인은 활성 페인의 폴더를 복제한다 (사용자 결정 2026-08-12). 여는 것은
    /// 아니다: 위치만 실어 두고 화면에 서는 순간 열린다 (<see cref="Show"/>) — 새 탭이
    /// <c>PendingLocation</c> 을 쓰는 것과 완전히 같은 길이다.
    /// </para>
    /// </summary>
    /// <returns>0번 페인. 생성자가 활성 페인을 잡는 자리다.</returns>
    private PaneTabsViewModel Grow(int count)
    {
        while (allPanes.Count < count)
        {
            var pane = new PaneTabsViewModel(paneFactory);

            // 한 번도 없던 자리다 — 기억이 없으므로 활성 페인을 복제한다. 0번은 복제할
            // 대상이 아직 없다 (생성자가 부르는 첫 호출).
            if (allPanes.Count > 0)
            {
                pane.ShowHiddenItems = activePane.ShowHiddenItems;
                pane.Active.PendingLocation = ActiveTab.CurrentLocation ?? ActiveTab.PendingLocation;
            }

            allPanes.Add(pane);

            // MRU 의 <b>맨 앞</b>(가장 오래된 자리)에 넣는다. 그래서 아직 한 번도 가 보지
            // 않은 페인들 사이에서는 <b>번호가 작은 쪽</b>이 '다른 페인' 이 된다 — 2분할에서
            // 그것이 정확히 예전의 "반대편" 이다 (0번에서 보면 1번).
            //
            // 여기서 빼먹으면 한 번도 활성이 된 적 없는 페인은 OtherPane 이 영영 못 찾는다.
            recent.Insert(0, pane);

            Wire(pane);
        }

        return allPanes[0];
    }

    /// <summary>
    /// 앞에서부터 <paramref name="count"/> 개를 화면에 세우고 나머지를 접는다.
    /// <para>
    /// <b>접기는 감시를 놓는 것이고 펴기는 여는 것이다</b> — 둘 다 페인이 안다
    /// (<c>PaneTabsViewModel.SuspendAsync</c>·<c>ResumeAsync</c>). 여기서는 어느 것이
    /// 보이는가만 정한다.
    /// </para>
    /// </summary>
    private Task Show(int count)
    {
        // 목록을 바꾸기 <b>전에</b> 누가 접히고 누가 펴지는지 정한다. 바꾸면서 세면 인덱스가
        // 발밑에서 움직인다.
        var folding = visiblePanes.Skip(count).ToArray();
        var unfolding = allPanes.Take(count).Skip(visiblePanes.Count).ToArray();

        Reveal(count);

        var work = new List<Task>(folding.Length + unfolding.Length);

        // 접는 것을 먼저 건다. 화면에서 빠지는 페인이 감시를 든 채 남으면 §13 의 폭주가
        // 보이지 않는 자리에서 돈다.
        work.AddRange(folding.Select(pane => pane.SuspendAsync()));
        work.AddRange(unfolding.Select(pane => pane.ResumeAsync()));

        return work.Count == 0 ? Task.CompletedTask : Task.WhenAll(work);
    }

    /// <summary>
    /// 보이는 목록만 <paramref name="count"/> 개로 맞춘다 — <b>여닫는 일은 하지 않는다.</b>
    /// <para>
    /// 조립과 복원이 이것을 쓴다. 그 둘에서는 폴더를 여는 주체가 따로 있고
    /// (<c>PaneTabsViewModel.RestoreAsync</c>), 여기서 또 열면 같은 폴더를 두 번 열거한다.
    /// </para>
    /// </summary>
    private void Reveal(int count)
    {
        for (var index = visiblePanes.Count - 1; index >= count; index--)
        {
            visiblePanes.RemoveAt(index);
        }

        for (var index = visiblePanes.Count; index < count; index++)
        {
            visiblePanes.Add(allPanes[index]);
        }

        splitCount = count;
        splitOptions = null;

        // 슬롯 넷은 파생 속성이라 값 비교로 걸러낼 수 없다 — 하나라도 빠뜨리면 그 칸만
        // 옛 페인을 계속 그린다.
        OnPropertyChanged(nameof(Pane0));
        OnPropertyChanged(nameof(Pane1));
        OnPropertyChanged(nameof(Pane2));
        OnPropertyChanged(nameof(Pane3));
        OnPropertyChanged(nameof(SplitOptions));
    }

    private IReadOnlyList<SplitOption> BuildSplitOptions() =>
    [
        new SplitOption(1, "1분할", splitCount == 1),
        new SplitOption(2, "2분할", splitCount == 2),
        new SplitOption(3, "3분할", splitCount == 3),
        new SplitOption(4, "4분할", splitCount == 4),
    ];

    /// <summary>슬롯 하나. 그 자리에 보이는 페인이 없으면 <see langword="null"/> 이다.</summary>
    private PaneTabsViewModel? Slot(int index)
        => index < visiblePanes.Count ? visiblePanes[index] : null;

    /// <summary>
    /// 페인 하나를 창에 잇는다. <b>만드는 자리가 하나여야 배선이 새거나 남지 않는다</b> —
    /// 페인이 런타임에 늘어나므로 조립 시점에 다 걸 수 없다.
    /// </summary>
    private void Wire(PaneTabsViewModel pane)
    {
        // 비활성 페인의 항목 클릭은 선택과 활성 전환이 한 동작이다 (목업 동작). View 가
        // 전환을 따로 쏘면 클릭 한 번에 바인딩 두 개가 경합한다.
        //
        // 무는 곳이 탭이 아니라 <b>페인</b>인 것은 탭이 들어올 때 정해졌다 — 탭마다
        // 구독하면 탭을 만들고 닫을 때마다 이 배선이 새거나 남는다.
        pane.ActivationRequested += (_, _) => Activate(pane);

        // 활성 탭이 바뀌면 XAML 이 물고 있는 자리가 통째로 바뀐다 (ActiveTab·OtherTab).
        pane.PropertyChanged += OnPaneTabsChanged;

        // 되살리기 스택은 창 전체에 하나다 (docs/PRD-v2.md §17). <b>어느 경로로 닫혔든</b>
        // 여기로 모인다 — Ctrl+W · 가운데 버튼 · 컨텍스트 메뉴의 닫기 셋이 각자 쌓으면
        // 하나를 빼먹는 순간 그 탭만 되살아나지 않는다.
        pane.TabClosed += (_, closed) => Remember(pane, closed);

        if (Tree is { } tree)
        {
            // 활성 페인이 옮기면 트리가 그 자리를 편다 (docs/PRD-v2.md §10-3 · 사용자 요청
            // 2026-08-10). 되먹임을 끊는 것은 트리 쪽이다 (FolderTreeViewModel 의 revealing).
            //
            // 페인이 "활성 탭이 보는 폴더가 바뀌었다" 를 모아 낸다 — 탭 전환도 그 신호다
            // (docs/PRD-v2.md §17 자명하게).
            pane.ActiveLocationChanged += OnPaneLocationChanged;

            // 목록 우클릭의 '즐겨찾기에 추가'. 페인은 트리를 모르므로 여기서 잇는다 —
            // 페인 간 복사를 워크스페이스가 잇는 것과 같은 자리다.
            pane.PinRequested += (_, paths) => _ = tree.AddFavoritesAsync(paths);
        }
    }

    /// <summary>이 페인을 MRU 의 끝(가장 최근)으로 올린다.</summary>
    private void Touch(PaneTabsViewModel pane)
    {
        recent.Remove(pane);
        recent.Add(pane);
    }

    /// <summary>
    /// 보이는 페인 중 가장 최근에 활성이었던 것. 활성 페인이 접혔을 때 갈 곳이다 —
    /// 화면에 하나는 반드시 남으므로 (<see cref="SetSplit"/> 이 0 을 만들지 않는다)
    /// 답이 없을 수 없다.
    /// </summary>
    private PaneTabsViewModel MostRecentVisible()
    {
        for (var index = recent.Count - 1; index >= 0; index--)
        {
            if (visiblePanes.Contains(recent[index]))
            {
                return recent[index];
            }
        }

        return visiblePanes[0];
    }

    /// <summary>
    /// 기억된 전역 상태. 실패하면 <see cref="GlobalViewState.Default"/> 로 조용히 넘어간다 —
    /// 전역 뷰 상태는 캐시다 (CLAUDE.md §4). 창이 뜨지 않는 것과는 전혀 다른 사건이다.
    /// </summary>
    private async ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
    {
        try
        {
            return await viewStates.LoadGlobalAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return GlobalViewState.Default;
        }
    }

    /// <summary>
    /// 페인이 보는 폴더가 바뀌면 트리를 그리로 편다 — <b>활성 탭의 이동과 탭 전환 둘 다</b>가
    /// 이 자리를 지난다 (docs/PRD-v2.md §17 자명하게). <b>활성 페인만</b> 본다 (사용자 결정
    /// 2026-08-10) — 다른 페인까지 따라가면 트리가 어느 것을 가리키는지 알 수 없고, 그 탐색은
    /// 전부 저장소 호출이다.
    /// </summary>
    private void OnPaneLocationChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, activePane))
        {
            RevealInTree();
        }
    }

    /// <summary>
    /// 활성 탭이 바뀌었다. <b>View 가 물고 있는 자리가 통째로 바뀐다</b> —
    /// <see cref="ActiveTab"/>·<see cref="OtherTab"/> 은 파생 속성이라 값 비교로 걸러낼 수 없다.
    /// </summary>
    private void OnPaneTabsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PaneTabsViewModel.Active))
        {
            return;
        }

        if (ReferenceEquals(sender, activePane))
        {
            OnPropertyChanged(nameof(ActiveTab));
        }
        else if (ReferenceEquals(sender, OtherPane))
        {
            OnPropertyChanged(nameof(OtherTab));
        }
    }

    /// <summary>
    /// 기억된 탭 목록에서 활성 탭의 폴더를 낸다 — 시작 폴더 규칙의 입력이다
    /// (<c>AppSettings.ResolveStartFolder</c> 의 <c>lastFolder</c>).
    /// </summary>
    private static Core.Locations.LocationId? ActiveFolder(PaneTabsState? state)
        => state is { Tabs.Count: > 0 } tabs ? tabs.Tabs[tabs.ActiveIndex].Folder : null;

    /// <summary>
    /// 활성 페인이 보는 폴더를 트리에서 편다 (docs/PRD-v2.md §10-3).
    /// <para>
    /// <b>기다리지 않는다</b> — 부르는 곳이 전부 속성 변경 알림이고, 여는 것은 저장소에
    /// 닿는다 (CLAUDE.md §3). 앞선 따라가기를 끊는 것은 트리가 한다 (<c>RevealAsync</c>).
    /// </para>
    /// </summary>
    private void RevealInTree()
    {
        if (Tree is { } tree && ActiveTab.CurrentLocation is { } folder)
        {
            treeRevealWork = tree.RevealAsync(folder);
        }
    }

    /// <summary>
    /// 저장된 설정. 패널이 배선되지 않았으면 기본값이다 — 그때 시작 폴더는 마지막 폴더로,
    /// 숨김은 감춤으로 간다.
    /// </summary>
    private async ValueTask<AppSettings> LoadSettingsAsync(CancellationToken ct)
    {
        if (Settings is not { } settings)
        {
            return AppSettings.Default;
        }

        // 던지지 않는다 (SettingsViewModel.LoadAsync) — 설정 파일 하나 때문에 창이 안 뜨면
        // 안 된다. 전역 상태를 읽을 때와 같은 판단이다.
        await settings.LoadAsync(ct).ConfigureAwait(false);

        return settings.Current;
    }

    /// <summary>
    /// 바뀐 숨김 정책을 페인들과 트리에 밀고 지금 보고 있는 것을 다시 읽는다.
    /// <para>
    /// <b>함께 기다린다.</b> 페인은 독립이고 트리는 곁다리라, 하나가 느려도(네트워크 폴더가
    /// 열려 있을 수 있다) 다른 것을 막을 이유가 없다 — <see cref="RestoreAsync"/> 가 폴더를
    /// 함께 여는 것과 같은 자리다.
    /// </para>
    /// </summary>
    private async Task ApplyHiddenItemsAsync()
    {
        if (Settings is not { } settings)
        {
            return;
        }

        var showHidden = settings.ShowHiddenItems;
        var work = new List<Task>(allPanes.Count + 1);

        // 접힌 페인에도 민다 — 정책은 페인이 소유하므로 (docs/PRD-v2.md §17 구조 렌즈)
        // 여기서 빠뜨리면 접었다 편 페인만 옛 정책으로 뜬다. 다시 <b>읽는</b> 것은 보이는
        // 페인의 활성 탭뿐이고, 나머지는 활성이 될 때의 새로 고침이 새 정책으로 읽는다.
        foreach (var pane in allPanes)
        {
            pane.ShowHiddenItems = showHidden;
        }

        foreach (var pane in visiblePanes)
        {
            work.Add(pane.Active.RefreshAsync());
        }

        if (Tree is { } tree)
        {
            tree.ShowHiddenItems = showHidden;

            work.Add(tree.ReloadFoldersAsync());
        }

        await Task.WhenAll(work).ConfigureAwait(false);
    }

    /// <summary>
    /// 비율을 허용 범위로 자른다. <c>Math.Clamp</c> 를 쓰지 않는 이유: NaN 은 모든 관계
    /// 비교가 false 라서 그대로 통과하고, 그러면 페인 하나가 사라진다.
    /// </summary>
    private static double Clamp(double value)
        => value is >= MinSplitterRatio and <= MaxSplitterRatio
            ? value
            : value > MaxSplitterRatio ? MaxSplitterRatio : MinSplitterRatio;
}
