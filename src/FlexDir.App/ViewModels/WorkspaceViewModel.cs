using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.Core.ViewState;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 페인의 자리. 좌/우 둘뿐이다 — 탭·4분할·세 번째 페인은 v2+ 다 (docs/PRD.md §3 · ADR-004).
/// </summary>
public enum PaneSide
{
    Left,
    Right,
}

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

    private readonly IViewStateStore viewStates;

    private PaneSide activeSide = PaneSide.Left;
    private double splitterRatio = GlobalViewState.Default.SplitterRatio;
    private WindowPlacement? windowPlacement;

    public WorkspaceViewModel(PaneViewModel left, PaneViewModel right, IViewStateStore viewStateStore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(viewStateStore);

        Left = left;
        Right = right;
        viewStates = viewStateStore;
    }

    public PaneViewModel Left { get; }

    public PaneViewModel Right { get; }

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
            }
        }
    }

    /// <summary>활성 페인. 키보드 조작이 향하는 곳이다.</summary>
    public PaneViewModel ActivePane => activeSide == PaneSide.Left ? Left : Right;

    /// <summary>활성이 아닌 페인. 페인 간 복사·이동의 대상이다.</summary>
    public PaneViewModel InactivePane => activeSide == PaneSide.Left ? Right : Left;

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

    /// <summary>저장된 전역 상태(스플리터 비율·창 배치)를 복원한다.</summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        var state = await LoadGlobalAsync(ct).ConfigureAwait(false);

        // 클램프를 지난다 — 저장된 값이 0.02 여도 페인 하나가 사라지지 않는다.
        SplitterRatio = state.SplitterRatio;
        WindowPlacement = state.Window;
    }

    /// <summary>현재 전역 상태를 저장한다.</summary>
    public async Task PersistAsync(CancellationToken ct = default)
    {
        try
        {
            await viewStates
                .SaveGlobalAsync(new GlobalViewState(SplitterRatio, WindowPlacement), ct)
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
    /// 비율을 허용 범위로 자른다. <c>Math.Clamp</c> 를 쓰지 않는 이유: NaN 은 모든 관계
    /// 비교가 false 라서 그대로 통과하고, 그러면 페인 하나가 사라진다.
    /// </summary>
    private static double Clamp(double value)
        => value is >= MinSplitterRatio and <= MaxSplitterRatio
            ? value
            : value > MaxSplitterRatio ? MaxSplitterRatio : MinSplitterRatio;
}
