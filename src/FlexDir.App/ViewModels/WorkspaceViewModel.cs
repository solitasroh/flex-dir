using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.Core.Settings;
using FlexDir.Core.ViewState;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 페인의 자리. 좌/우 둘뿐이다 — 4분할과 세 번째 페인은 v2+ 다 (docs/PRD.md §3 · ADR-004).
/// <para>
/// <b>탭은 이 열거형을 늘리지 않는다</b> (docs/PRD-v2.md §17 · ADR-018). 탭은 자리마다
/// <see cref="PaneTabsViewModel"/> 하나가 목록으로 갖는다 — 창 단위가 아니라 페인 단위라
/// 좌·우가 각자의 탭 목록을 든다.
/// </para>
/// </summary>
public enum PaneSide
{
    Left,
    Right,
}

/// <summary>
/// 닫은 탭 하나 — 어느 페인의 어느 자리였는지까지 (docs/PRD-v2.md §17 되살리기).
/// 스택이 창 전체에 하나이므로 페인을 함께 들어야 원래 자리로 돌아갈 수 있다.
/// </summary>
internal readonly record struct ClosedTabRecord(PaneSide Side, ClosedTab Closed);

/// <summary>
/// 창 전체. 페인 둘과 활성 페인, 그리고 폴더와 무관한 전역 상태를 쥔다.
/// <para>
/// 두 페인은 완전히 독립이다 (docs/PRD.md §4) — 여기에는 한쪽 조작을 다른 쪽으로 옮기는
/// 경로가 <see cref="OpenOtherPaneLocationAsync"/> 하나뿐이고, 그것도 사용자가 명령을
/// 실행할 때만 움직인다. 공용 정렬·공용 선택 같은 상태를 두지 않는다.
/// </para>
/// <para>
/// 창 배치는 <see cref="WindowPlacement"/> 로 노출하는 것까지가 범위다. 실제로 창에 적용하는
/// 것은 View 의 일이다 — App 의 ViewModel 은 WPF 창을 만지지 않는다 (docs/ARCHITECTURE.md §1).
/// </para>
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    /// <summary>
    /// 좌 페인 비율의 하한·상한. 창 최소 너비 900 에서 페인 최소 너비 320 을 지키려면
    /// 비율이 양 끝으로 갈 수 없다 (docs/DESIGN.md §1).
    /// </summary>
    private const double MinSplitterRatio = 0.15;

    private const double MaxSplitterRatio = 0.85;

    /// <summary>
    /// 되살릴 수 있는 닫은 탭의 수 (docs/PRD-v2.md §17). 종료하면 비운다 — 프로세스
    /// 수명이라 별도로 지울 자리가 없다.
    /// </summary>
    private const int ReopenDepth = 10;

    private readonly IViewStateStore viewStates;

    /// <summary>
    /// 닫은 탭. <b>창 전체에 하나다</b> (docs/PRD-v2.md §17) — 끝이 가장 최근이다.
    /// 스택 타입을 쓰지 않는 이유: 깊이를 넘길 때 <b>가장 오래된 것</b>을 버려야 하고
    /// <c>Stack&lt;T&gt;</c> 는 바닥에 닿는 길이 없다.
    /// </summary>
    private readonly List<ClosedTabRecord> closedTabs = [];

    private PaneSide activeSide = PaneSide.Left;
    private double splitterRatio = GlobalViewState.Default.SplitterRatio;
    private WindowPlacement? windowPlacement;

    /// <summary>숨김 정책이 바뀌어 다시 읽는 중인 작업. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    private Task hiddenItemsWork = Task.CompletedTask;

    /// <summary>진행 중인 트리 따라가기. 테스트가 "끝났는가" 를 보는 자리다.</summary>
    private Task treeRevealWork = Task.CompletedTask;

    /// <param name="update">
    /// 새 버전 알림 (docs/PRD-v2.md §9). <b>선택이다</b> — 없으면 알림 바가 영영 접혀 있고
    /// 나머지는 그대로 돈다. 창은 업데이트 없이도 서야 한다: 조립이 그 자리에서 막히면
    /// 업데이트와 무관한 앱 전체가 못 뜬다.
    /// </param>
    /// <param name="tree">
    /// 왼쪽 폴더 트리 (docs/PRD-v2.md §10). <paramref name="update"/> 와 같은 이유로
    /// 선택이다 — 두 페인은 트리 없이도 돌아야 하고, 트리를 보지 않는 테스트가 드라이브
    /// 열거 fake 까지 조립하게 만들 이유가 없다.
    /// </param>
    /// <param name="settings">
    /// 설정 패널 (docs/PRD-v2.md §12). 앞의 둘과 같은 이유로 선택이다 — 없으면 시작 폴더는
    /// 마지막 폴더로, 숨김 정책은 기본값으로 간다.
    /// </param>
    /// <param name="paneFactory">
    /// 탭 하나를 만드는 법 (docs/PRD-v2.md §17). <b>페인 둘이 아니라 팩토리를 받는다</b> —
    /// 탭이 런타임에 늘어나므로 조립 시점에 인스턴스를 다 알 수 없다. 조립은 이미 열려
    /// 있었다: <c>AppComposition.Create</c> 의 <c>Pane()</c> 지역 함수가 그대로 이것이다.
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

        LeftTabs = new PaneTabsViewModel(paneFactory);
        RightTabs = new PaneTabsViewModel(paneFactory);
        Update = update;
        Tree = tree;
        Settings = settings;
        viewStates = viewStateStore;

        if (settings is not null)
        {
            // 상태 폴더 열기. 트리의 NavigationRequested 와 같은 자리다 — 설정은 페인을
            // 모르고, 어느 페인이 갈지는 여기서 정한다.
            settings.NavigationRequested += (_, location) => _ = ActivePane.NavigateAsync(location);

            // 숨김 정책이 바뀌면 지금 보고 있는 것을 다시 걸러야 한다. 페인과 트리가
            // 스스로 하지 않는 이유는 각자의 속성 주석에 있다 — 다시 읽을지를 정하는
            // 곳이 하나여야 클릭 한 번에 저장소 호출이 셋으로 늘지 않는다.
            settings.HiddenItemsChanged += (_, _) => hiddenItemsWork = ApplyHiddenItemsAsync();
        }

        // 비활성 페인의 항목 클릭은 선택과 활성 전환이 한 동작이다 (목업 동작). View 가
        // 전환을 따로 쏘면 클릭 한 번에 바인딩 두 개가 경합한다.
        //
        // 탭이 들어와도 무는 곳은 여전히 <b>페인 둘</b>이다 — 탭마다 구독하면 탭을 만들고
        // 닫을 때마다 이 배선이 새거나 남는다. 페인이 자기 탭들의 신호를 모아 낸다.
        LeftTabs.ActivationRequested += (_, _) => ActiveSide = PaneSide.Left;
        RightTabs.ActivationRequested += (_, _) => ActiveSide = PaneSide.Right;

        // 활성 탭이 바뀌면 XAML 이 물고 있는 자리가 통째로 바뀐다 (Left·Right·ActivePane).
        LeftTabs.PropertyChanged += OnPaneTabsChanged;
        RightTabs.PropertyChanged += OnPaneTabsChanged;

        // 되살리기 스택은 창 전체에 하나다 (docs/PRD-v2.md §17). <b>어느 경로로 닫혔든</b>
        // 여기로 모인다 — Ctrl+W · 가운데 버튼 · 컨텍스트 메뉴의 닫기 셋이 각자 쌓으면
        // 하나를 빼먹는 순간 그 탭만 되살아나지 않는다.
        LeftTabs.TabClosed += (_, closed) => Remember(PaneSide.Left, closed);
        RightTabs.TabClosed += (_, closed) => Remember(PaneSide.Right, closed);

        if (tree is not null)
        {
            // 어느 페인이 갈지는 트리가 아니라 여기서 정한다 (사용자 결정 2026-08-10:
            // 활성 페인). 트리가 페인을 직접 알면 창 하나에 트리 하나라는 전제가 트리
            // 안으로 새어 든다.
            //
            // 기다리지 않는다 — 이벤트 핸들러는 동기이고, 페인은 열거 실패를 자기
            // 상태표시줄에 낸다 (PaneViewModel.FillAsync).
            tree.NavigationRequested += (_, location) => _ = ActivePane.NavigateAsync(location);

            // 반대 방향 — 활성 페인이 옮기면 트리가 그 자리를 편다 (docs/PRD-v2.md §10-3 ·
            // 사용자 요청 2026-08-10). 위의 배선과 짝이 되어 고리를 이루므로, 되먹임을
            // 끊는 것은 트리 쪽이다 (FolderTreeViewModel 의 revealing).
            //
            // 페인이 "활성 탭이 보는 폴더가 바뀌었다" 를 모아 낸다 — 탭 전환도 그 신호다
            // (docs/PRD-v2.md §17 자명하게).
            LeftTabs.ActiveLocationChanged += OnPaneLocationChanged;
            RightTabs.ActiveLocationChanged += OnPaneLocationChanged;

            // 목록 우클릭의 '즐겨찾기에 추가'. 페인은 트리를 모르므로 여기서 잇는다 —
            // 페인 간 복사를 워크스페이스가 잇는 것과 같은 자리다.
            LeftTabs.PinRequested += (_, paths) => _ = tree.AddFavoritesAsync(paths);
            RightTabs.PinRequested += (_, paths) => _ = tree.AddFavoritesAsync(paths);
        }
    }

    /// <summary>좌 페인의 탭 목록 (docs/PRD-v2.md §17). 탭 줄이 이것을 그린다.</summary>
    public PaneTabsViewModel LeftTabs { get; }

    /// <summary>우 페인의 탭 목록. 좌·우가 독립된 목록을 갖는다 (ADR-018).</summary>
    public PaneTabsViewModel RightTabs { get; }

    /// <summary>
    /// 좌 페인의 <b>활성 탭</b>. View 가 물고 있는 자리이고, 탭이 바뀌면 이 속성이 바뀐다.
    /// </summary>
    public PaneViewModel Left => LeftTabs.Active;

    /// <inheritdoc cref="Left"/>
    public PaneViewModel Right => RightTabs.Active;

    /// <summary>
    /// 새 버전 알림. 폴더와 무관한 전역 상태라 여기 산다 — 페인이 둘인데 알림은 하나다.
    /// 배선되지 않았으면 <see langword="null"/> 이고 알림 바는 접혀 있다.
    /// </summary>
    public UpdateViewModel? Update { get; }

    /// <summary>
    /// 왼쪽 폴더 트리. 페인이 둘인데 트리는 하나다 — 알림과 같은 자리에 산다.
    /// 배선되지 않았으면 <see langword="null"/> 이고 View 가 그 열을 접는다.
    /// </summary>
    public FolderTreeViewModel? Tree { get; }

    /// <summary>
    /// 설정 패널 (docs/PRD-v2.md §12). 알림·트리와 같은 자리에 산다 — 페인이 둘인데
    /// 설정은 하나다. 배선되지 않았으면 <see langword="null"/> 이고 오버레이가 뜨지 않는다.
    /// </summary>
    public SettingsViewModel? Settings { get; }

    /// <summary>
    /// UI 스레드 사고 알림 (docs/PRD-v2.md §14). 페인이 둘인데 알림은 하나라 위의 셋과
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

    /// <summary>활성 페인은 항상 정확히 하나다. 어느 쪽인지를 이 값 하나로 정한다.</summary>
    public PaneSide ActiveSide
    {
        get => activeSide;
        private set
        {
            if (SetProperty(ref activeSide, value))
            {
                // 파생 속성이라 값 비교로 걸러낼 수 없다. View 가 활성 표시를 여기에
                // 바인딩한다 (docs/DESIGN.md §6).
                OnPropertyChanged(nameof(ActivePane));
                OnPropertyChanged(nameof(InactivePane));
                OnPropertyChanged(nameof(ActiveTabs));
                OnPropertyChanged(nameof(InactiveTabs));

                // 가리키는 페인이 바뀌었으니 트리도 옮겨간다 — Tab 으로 옮긴 뒤에도 트리가
                // 반대쪽 자리를 가리키면 어느 쪽을 보고 있는지 알 수 없다.
                RevealInTree();
            }
        }
    }

    /// <summary>활성 페인의 탭 목록. 키보드 탭 조작(<c>Ctrl+T</c>·<c>Ctrl+W</c>)이 향하는 곳이다.</summary>
    public PaneTabsViewModel ActiveTabs => activeSide == PaneSide.Left ? LeftTabs : RightTabs;

    /// <summary>활성이 아닌 페인의 탭 목록.</summary>
    public PaneTabsViewModel InactiveTabs => activeSide == PaneSide.Left ? RightTabs : LeftTabs;

    /// <summary>
    /// 활성 페인의 <b>활성 탭</b>. 키보드 조작이 향하는 곳이다 — 탭이 들어오며 한 단
    /// 깊어진 자리가 여기다 (docs/PRD-v2.md §17 · ADR-018).
    /// </summary>
    public PaneViewModel ActivePane => ActiveTabs.Active;

    /// <summary>활성이 아닌 페인의 활성 탭. 페인 간 복사·이동의 대상이다.</summary>
    public PaneViewModel InactivePane => InactiveTabs.Active;

    /// <summary>
    /// 좌 페인이 차지하는 비율. 범위를 벗어난 값은 예외 없이 가장 가까운 경계로 잘린다 —
    /// 스플리터를 끝까지 끌거나 저장 파일이 손상돼도 페인이 사라지면 안 된다.
    /// </summary>
    public double SplitterRatio
    {
        get => splitterRatio;

        // 자르고 나서 비교한다. 경계를 넘어 계속 끌어도 알림은 한 번이다.
        set => SetProperty(ref splitterRatio, Clamp(value));
    }

    /// <summary>복원·저장 대상 창 배치. 기억된 것이 없으면 null 이다.</summary>
    public WindowPlacement? WindowPlacement
    {
        get => windowPlacement;
        set => SetProperty(ref windowPlacement, value);
    }

    /// <summary>
    /// 저장된 전역 상태(스플리터 비율·창 배치·마지막 폴더)를 복원한다.
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

        // 페인에 민다. 소유자가 페인이므로 복원된 탭과 앞으로 만들 탭이 모두 이 정책으로
        // 뜬다 (docs/PRD-v2.md §17 구조 렌즈 — 예전에는 나중에 만든 탭이 기본값으로 떴다).
        LeftTabs.ShowHiddenItems = settings.ShowHiddenItems;
        RightTabs.ShowHiddenItems = settings.ShowHiddenItems;

        // 클램프를 지난다 — 저장된 값이 0.02 여도 페인 하나가 사라지지 않는다.
        SplitterRatio = state.SplitterRatio;
        WindowPlacement = state.Window;

        // 컬럼 폭은 페인마다 따로다 (사용자 지적 2026-08-10) — 한쪽에서 끌 때 반대편이
        // 함께 움직이면 안 된다. 한 페인 <b>안의</b> 탭들은 이것을 나눠 쓴다.
        LeftTabs.Columns = state.LeftColumns ?? PaneColumns.Default;
        RightTabs.Columns = state.RightColumns ?? PaneColumns.Default;

        var opens = new List<Task>(3);

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

        // 시작 폴더 규칙은 Core 에 있다 (AppSettings.ResolveStartFolder) — 페인마다 한 번씩
        // 같은 규칙을 지난다. 규칙이 정하는 것은 <b>활성 탭</b>이 열 폴더이고, 배경 탭은
        // 기억된 자기 폴더를 그대로 든다 (docs/PRD-v2.md §17 세션 복원).
        opens.Add(LeftTabs.RestoreAsync(
            state.LeftTabs,
            settings.ResolveStartFolder(ActiveFolder(state.LeftTabs), fallbackFolder),
            ct));

        opens.Add(RightTabs.RestoreAsync(
            state.RightTabs,
            settings.ResolveStartFolder(ActiveFolder(state.RightTabs), fallbackFolder),
            ct));

        // 함께 연다 — 페인은 독립이라 한쪽이 느려도 (네트워크·대용량) 다른 쪽을 막지 않는다.
        await Task.WhenAll(opens).ConfigureAwait(false);
    }

    /// <summary>현재 전역 상태를 저장한다. 마지막 폴더가 다음 실행의 시작 폴더다.</summary>
    public async Task PersistAsync(CancellationToken ct = default)
    {
        try
        {
            await viewStates
                .SaveGlobalAsync(
                    new GlobalViewState(
                        SplitterRatio,
                        WindowPlacement,
                        LeftTabs.Capture(),
                        RightTabs.Capture(),
                        Tree?.IsVisible ?? true,
                        Tree?.Width ?? GlobalViewState.DefaultTreeWidth,
                        LeftTabs.Columns,
                        RightTabs.Columns),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 읽기와 같은 이유로 삼킨다 — 저장 실패가 창을 닫는 길을 막으면 안 된다.
        }
    }

    /// <summary>
    /// 활성 페인을 지정한다. 선택은 어느 쪽도 건드리지 않는다 — 비활성 페인의 선택은 그대로
    /// 남고 표시만 강등된다(<c>--sel-inactive</c>). 그것은 View 의 일이다.
    /// </summary>
    [RelayCommand]
    private void Activate(PaneSide side) => ActiveSide = side;

    /// <summary>키보드 페인 전환. 반대쪽으로 <see cref="Activate"/> 하는 것과 같다.</summary>
    [RelayCommand]
    private void SwitchPane()
        => Activate(activeSide == PaneSide.Left ? PaneSide.Right : PaneSide.Left);

    // ── 탭 (docs/PRD-v2.md §17) ───────────────────────────────────
    //
    // 키보드는 활성 페인으로 간다 (Tab·F6 은 페인 전환 그대로다). 판단은 전부
    // PaneTabsViewModel 안에 있고 여기서 정하는 것은 <b>어느 페인</b>인가 뿐이다 — 페인 간
    // 복사가 "어디로" 만 정하는 것과 같은 구도다.

    /// <summary>
    /// 새 탭 (<c>Ctrl+T</c>). 활성 페인에, 지금 폴더를 복제해서 활성 탭 바로 오른쪽에.
    /// </summary>
    [RelayCommand]
    private void NewTab() => ActiveTabs.NewTab();

    /// <summary>
    /// 활성 탭 닫기 (<c>Ctrl+W</c>). 탭이 1개거나 고정 탭이면 무시된다 — 그 판정은 페인이
    /// 하고, 되살리기 스택은 <c>TabClosed</c> 를 타고 저절로 채워진다 (위 조립 참조).
    /// </summary>
    [RelayCommand]
    private Task CloseTabAsync() => ActiveTabs.CloseAsync(ActivePane);

    /// <summary>
    /// 활성 탭을 반대편 페인으로 보낸다 (컨텍스트 메뉴 '반대편 페인으로 보내기' ·
    /// docs/PRD-v2.md §17). <b>살아 있는 인스턴스의 소유권이 페인을 건너간다</b> — 감시
    /// 구독·썸네일 스케줄러·열거 세션·히스토리·선택이 전부 따라온다.
    /// <para>
    /// <b>활성 페인은 따라가지 않는다</b> (사용자 결정 2026-08-11). 보내는 것은 정리
    /// 동작이라 하던 일이 있는 페인에 남는 편이 연속으로 정리하기 쉽다 — 되살리기가 페인을
    /// 옮기는 것과 갈리는 지점이고, 그쪽은 "방금 그것을 원해서 누른 키" 다.
    /// </para>
    /// <para>
    /// 마지막 탭은 보낼 수 없다 — 그러면 그 페인이 탭 0개가 된다. 그 판정은 페인이 한다.
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
        // 어느 페인의 것인가는 목록이 안다. 떼기와 받기의 짝은 페인이 쥔다 — 탭 드래그가
        // 같은 짝을 쓴다 (PaneTabsViewModel.SendAsync).
        var target = tab ?? ActivePane;

        if (LeftTabs.Tabs.Contains(target))
        {
            return LeftTabs.SendAsync(target, RightTabs);
        }

        return RightTabs.Tabs.Contains(target) ? RightTabs.SendAsync(target, LeftTabs) : Task.CompletedTask;
    }

    /// <summary>다음 탭 (<c>Ctrl+Tab</c> · <c>Ctrl+PageDown</c>). 활성 페인 안에서 순환한다.</summary>
    [RelayCommand]
    private void NextTab() => ActiveTabs.Next();

    /// <summary>이전 탭 (<c>Ctrl+Shift+Tab</c> · <c>Ctrl+PageUp</c>).</summary>
    [RelayCommand]
    private void PreviousTab() => ActiveTabs.Previous();

    /// <summary>
    /// 닫은 탭 되살리기 (<c>Ctrl+Shift+T</c>). <b>창 전체에 스택 하나</b>다
    /// (docs/PRD-v2.md §17) — 실수로 닫은 것이 어느 페인이었는지 기억하지 않아도 된다.
    /// 페인별 스택이면 닫은 직후 반대편으로 옮겨가 누르면 돌아오지 않는다.
    /// <para>
    /// 되살린 탭이 활성이 되고 <b>그 페인도 활성이 된다</b> — 방금 그것을 원해서 누른
    /// 키다 (사용자 렌즈가 잡은 것). 그러지 않으면 화면 밖에서 조용히 생긴다.
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

        (last.Side == PaneSide.Left ? LeftTabs : RightTabs).Insert(last.Closed.Index, last.Closed.State);

        ActiveSide = last.Side;
    }

    /// <summary>
    /// 닫은 탭을 스택에 쌓는다. 깊이를 넘으면 <b>가장 오래된 것</b>을 버린다 —
    /// 되살리기는 최근 것부터 꺼낸다.
    /// </summary>
    private void Remember(PaneSide side, ClosedTab closed)
    {
        closedTabs.Add(new ClosedTabRecord(side, closed));

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
        if (under is not null && LeftTabs.Tabs.Contains(under))
        {
            ActiveSide = PaneSide.Left;

            return LeftTabs.Active;
        }

        if (under is not null && RightTabs.Tabs.Contains(under))
        {
            ActiveSide = PaneSide.Right;

            return RightTabs.Active;
        }

        return ActivePane;
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
    private void UseCurrentFolderAsStart() => Settings?.UseCurrentFolder(ActivePane.CurrentLocation);

    /// <summary>
    /// 활성 페인이 보고 있는 폴더를 트리에 고정한다 (docs/PRD-v2.md §10-2).
    /// 아무 곳도 열지 않았거나 트리가 없으면 아무 일도 하지 않는다 — 툴바 버튼은 그런
    /// 상태에서도 눌린다.
    /// </summary>
    [RelayCommand]
    private Task PinCurrentFolderAsync(CancellationToken ct)
    {
        return Tree is { } tree && ActivePane.CurrentLocation is { } folder
            ? tree.AddFavoriteAsync(folder, ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 반대편 페인의 현재 폴더를 활성 페인에서 연다 — 2분할의 존재 이유가 이 왕복이다.
    /// 히스토리에 기록하므로 뒤로가 원래 폴더로 돌아간다.
    /// </summary>
    [RelayCommand]
    private Task OpenOtherPaneLocationAsync()
    {
        // 반대편이 아직 아무 곳도 열지 않았으면 옮길 폴더가 없다.
        return InactivePane.CurrentLocation is { } location
            ? ActivePane.NavigateAsync(location)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 활성 페인의 선택을 반대편 페인의 현재 폴더로 복사한다 — 2분할이 있는 이유다
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
        // 반대편이 아직 아무 곳도 열지 않았으면 보낼 곳이 없다.
        return InactivePane.CurrentLocation is { } destination
            ? ActivePane.CopySelectionToAsync(destination, ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 활성 페인의 선택을 반대편 페인의 현재 폴더로 이동한다. 양쪽이 같은 폴더면
    /// 아무 일도 하지 않는다 (제자리 이동).
    /// </summary>
    [RelayCommand]
    private Task MoveToOtherPaneAsync(CancellationToken ct)
    {
        return InactivePane.CurrentLocation is { } destination
            ? ActivePane.MoveSelectionToAsync(destination, ct)
            : Task.CompletedTask;
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
    /// 2026-08-10) — 반대편까지 따라가면 트리가 어느 쪽을 가리키는지 알 수 없고, 그 탐색은
    /// 전부 저장소 호출이다.
    /// </summary>
    private void OnPaneLocationChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, ActiveTabs))
        {
            RevealInTree();
        }
    }

    /// <summary>
    /// 활성 탭이 바뀌었다. <b>View 가 물고 있는 자리가 통째로 바뀐다</b> —
    /// <see cref="Left"/>·<see cref="Right"/>·<see cref="ActivePane"/> 는 전부 파생
    /// 속성이라 값 비교로 걸러낼 수 없다.
    /// </summary>
    private void OnPaneTabsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PaneTabsViewModel.Active))
        {
            return;
        }

        OnPropertyChanged(ReferenceEquals(sender, LeftTabs) ? nameof(Left) : nameof(Right));

        if (ReferenceEquals(sender, ActiveTabs))
        {
            OnPropertyChanged(nameof(ActivePane));
        }
        else
        {
            OnPropertyChanged(nameof(InactivePane));
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
        if (Tree is { } tree && ActivePane.CurrentLocation is { } folder)
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
    /// 바뀐 숨김 정책을 페인 둘과 트리에 밀고 지금 보고 있는 것을 다시 읽는다.
    /// <para>
    /// <b>함께 기다린다.</b> 페인은 독립이고 트리는 곁다리라, 한쪽이 느려도(네트워크 폴더가
    /// 열려 있을 수 있다) 다른 쪽을 막을 이유가 없다 — <see cref="RestoreAsync"/> 가 폴더
    /// 셋을 함께 여는 것과 같은 자리다.
    /// </para>
    /// </summary>
    private async Task ApplyHiddenItemsAsync()
    {
        if (Settings is not { } settings)
        {
            return;
        }

        var showHidden = settings.ShowHiddenItems;

        // 페인에 민다 — 모든 탭이 새 정책을 갖는다. 다시 <b>읽는</b> 것은 활성 탭 둘뿐이다:
        // 배경 탭은 활성이 될 때 도는 새로 고침이 새 정책으로 다시 읽는다 (ADR-018). 여기서
        // 전부 읽으면 클릭 한 번에 저장소 호출이 탭 수만큼 나간다.
        LeftTabs.ShowHiddenItems = showHidden;
        RightTabs.ShowHiddenItems = showHidden;

        var work = new List<Task>(3) { Left.RefreshAsync(), Right.RefreshAsync() };

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
