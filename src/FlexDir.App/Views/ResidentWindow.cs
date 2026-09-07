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
                // 이 이벤트는 UI 밖 스레드에서 온다 — 복원 사슬이 ConfigureAwait(false) 다.
                // 날 이벤트에는 바인딩 엔진의 마샬링이 없어서, 여기서 창을 바로 만지면
                // 스레드 친화성 예외가 StartAsync 의 버림 속으로 삼켜져 창이 영영 안 뜬다
                // (실물에서 그랬다).
                window.Dispatcher.InvokeAsync(() => Apply(window, workspace.WindowPlacement));
            }
        };

        // 숨겼다 다시 보였는가 — 그 사이에 사용자가 외부 도구를 설치하거나 지웠을 수 있다
        // (ADR-003). 판단은 저쪽에 있다 (WorkspaceViewModel.OnWindowShown): 여기서 재는 것은
        // 창이 보이게 됐다는 사실 하나뿐이다.
        //
        // Activated 가 아니다. 그것은 다른 앱에서 돌아올 때마다 뜨고, 알트탭 한 번에
        // 레지스트리 조회가 탭 수만큼 나간다. 첫 표시도 이것으로 덮인다.
        window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                workspace.OnWindowShown();
            }
        };

        window.Closing += (_, e) =>
        {
            // 배치는 닫는 순간의 것이 정본이다.
            if (Capture(window.RestoreBounds, window.WindowState) is { } placement)
            {
                workspace.WindowPlacement = placement;
            }

            e.Cancel = true;
            window.Hide();

            // 여기서도 남긴다 (2026-08-10). 한동안 완전 종료만 저장했는데, 상주
            // 프로세스에서 그것은 사실상 일어나지 않는 사건이라 마지막 폴더·창 배치가
            // 영영 갱신되지 않았다 — 실물에서 저장 파일이 사흘째 같은 값이었고 매 실행이
            // 폴백 폴더로 열렸다.
            //
            // 기다리지 않는다. 닫기는 즉시 끝나야 하고, PersistAsync 는 실패를 스스로
            // 삼킨다 (저장 실패가 창을 닫는 길을 막으면 안 된다).
            _ = workspace.PersistAsync();
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

    /// <summary>
    /// 복원이 창에 <b>처음</b> 거는 상태. 최대화는 창이 뜬 뒤로 미룬다.
    /// <para>
    /// 뜨기 전의 창에는 아직 모니터가 없다 — 그 상태에서 <c>WindowState.Maximized</c> 를
    /// 걸면 WPF 가 <b>주 모니터</b>의 작업영역으로 크기를 정하고, 방금 넣은
    /// <c>Left</c>/<c>Top</c> 은 버려진다. 보조 모니터에 최대화해 두고 껐다 켜면 창이
    /// 통째로 주 모니터로 옮겨가 있었다 (2026-08-12 실측).
    /// </para>
    /// </summary>
    internal static WindowState FirstState(WindowState restored, bool alreadyShown)
        => restored == WindowState.Maximized && !alreadyShown ? WindowState.Normal : restored;

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
        window.WindowState = FirstState(state, window.IsLoaded);

        if (state != WindowState.Maximized || window.IsLoaded)
        {
            return;
        }

        // 뜬 뒤에 최대화한다 (FirstState). 두 번 걸리지 않게 먼저 뗀다 — 복원은 생성 때와
        // 배치가 도착할 때 두 번 올 수 있다.
        window.Loaded -= Maximize;
        window.Loaded += Maximize;
    }

    private static void Maximize(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;

        window.Loaded -= Maximize;
        window.WindowState = WindowState.Maximized;
    }
}
