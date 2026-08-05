using System.ComponentModel;
using System.Windows;

using FlexDir.App.ViewModels;

using FlexDir.Core.ViewState;

namespace FlexDir.App.Views;

/// <summary>
/// 창에 상주 프로세스의 규약(ADR-003)을 건다: 배치 복원, 그리고 <b>닫기 = 숨기기</b>.
/// <para>
/// 닫기를 숨기기로 바꾸지 않으면 상주가 통째로 깨진다 — WPF 창은 한 번 닫히면 다시 보일
/// 수 없어서, X 로 닫은 뒤의 두 번째 실행이 <c>Show()</c> 에서 던진다. 완전 종료
/// (<c>Application.Shutdown</c>)는 <c>Closing</c> 취소를 무시하므로 이 훅이 종료를 막지
/// 않는다.
/// </para>
/// <para>
/// 배치를 창에 적용하는 것은 View 의 일이라 여기가 자리다 (<c>WorkspaceViewModel</c> 은
/// WPF 창을 만지지 않는다). 접기/펴기 판정(<see cref="Capture"/>·<see cref="Expand"/>)만
/// 채점하고, 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class ResidentWindow
{
    public static void Attach(Window window, WorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(workspace);

        Apply(window, workspace.WindowPlacement);

        // 복원은 창을 만든 뒤에 온다 (Program 이 만들고 StartAsync 가 복원한다). 배치가
        // 도착하면 받아 적는다 — 첫 표시는 복원 뒤이므로 (복원 → 활성화 순서) 창이 뛰지 않는다.
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.WindowPlacement))
            {
                Apply(window, workspace.WindowPlacement);
            }
        };

        window.Closing += (_, e) =>
        {
            // 배치는 닫는 순간의 것이 정본이다 — 완전 종료가 저장하는 값이 이것이다.
            if (Capture(window.RestoreBounds, window.WindowState) is { } placement)
            {
                workspace.WindowPlacement = placement;
            }

            e.Cancel = true;
            window.Hide();
        };
    }

    /// <summary>
    /// 창의 지금 모습을 배치로 접는다. 보인 적 없는 창(<c>RestoreBounds</c> 가 Empty)은
    /// <c>null</c> — 크기 0 을 저장하면 다음 실행에서 창이 보이지 않는다.
    /// <para>
    /// 최대화는 <c>RestoreBounds</c>(풀었을 때의 자리)와 함께 남기고, 최소화는 보통 창으로
    /// 접는다 — 최소화로 복원된 창은 뜨지 않은 것과 구별되지 않는다.
    /// </para>
    /// </summary>
    internal static WindowPlacement? Capture(Rect restoreBounds, WindowState state)
        => restoreBounds.IsEmpty || restoreBounds.Width <= 0 || restoreBounds.Height <= 0
            ? null
            : new WindowPlacement(
                restoreBounds.X,
                restoreBounds.Y,
                restoreBounds.Width,
                restoreBounds.Height,
                state == WindowState.Maximized);

    /// <summary>배치를 창의 좌표와 상태로 편다. <see cref="Capture"/> 의 역이다.</summary>
    internal static (Rect Bounds, WindowState State) Expand(WindowPlacement placement)
        => (new Rect(placement.X, placement.Y, placement.Width, placement.Height),
            placement.Maximized ? WindowState.Maximized : WindowState.Normal);

    private static void Apply(Window window, WindowPlacement? placement)
    {
        if (placement is null)
        {
            // 기억이 없다 — XAML 기본 크기와 OS 기본 위치로 뜬다.
            return;
        }

        var (bounds, state) = Expand(placement);

        // 수동 배치다 — 시작 위치 규칙(CenterScreen 류)이 좌표를 덮으면 복원이 아니다.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.X;
        window.Top = bounds.Y;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.WindowState = state;
    }
}
