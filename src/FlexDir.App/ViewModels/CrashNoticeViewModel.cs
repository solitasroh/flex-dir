using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FlexDir.App.ViewModels;

/// <summary>
/// UI 스레드 예외를 살아남는 자리 (docs/PRD-v2.md §14).
///
/// <para>
/// <b>상주 앱이라 값이 다르다</b> (ADR-003). 창 하나짜리 앱이면 예외 하나에 죽는 것이
/// 정직하지만, 여기서는 그 하나가 <b>트레이 아이콘과 상주 프로세스까지</b> 날린다 —
/// 2026-08-10 의 XAML 크래시가 정확히 그랬다 (docs/PRD-v2.md §10-2 §값을 치르고 배운 것).
/// 사용자는 우클릭 한 번에 앱이 사라진 것을 보고, 다시 켜야 한다.
/// </para>
///
/// <para>
/// <b>그렇다고 무조건 삼키지는 않는다</b> (사용자 결정 2026-08-11). <c>LayoutUpdated</c>
/// 같은 자리의 버그는 매 프레임 터지고 (<c>Views/VisibleRangeSync</c>), 그것을 전부
/// 삼키면 앱이 죽는 대신 예외를 쏟아내며 느려진다 — <b>증상이 더 안 보이는 모양</b>이 된다.
/// §13 이 겪은 "처방이 증상을 다시 만드는 고리" 와 같은 종류다. 그래서
/// <see cref="RecoveryWindow"/> 안에서 <see cref="MaxRecoveries"/> 번까지만 살리고,
/// 넘으면 오늘과 같이 죽는다. 그때까지의 기록은 이미 <c>error.log</c> 에 쌓였다.
/// </para>
///
/// <para>
/// <b>여기에는 예외의 종류를 가르는 자리가 없다.</b> "이건 삼켜도 되고 저건 안 된다" 는
/// 목록은 만드는 순간 틀리고, 그 목록을 유지할 근거가 하나도 없다. 무엇이 왔는지는
/// 로그가 말하고, 살릴지는 <b>얼마나 자주 오는가</b>로만 정한다.
/// </para>
/// </summary>
public sealed partial class CrashNoticeViewModel : ObservableObject
{
    /// <summary>
    /// 문구가 가리키는 파일. 실제로 쓰는 것은 <c>Host/Diagnostics/ErrorLog</c> 이고
    /// 이름이 갈리지 않는 것은 <c>ErrorLogTests</c> 가 고정한다 — 참조 방향이 Host → App
    /// 이라 App 에서 그 상수를 볼 수 없다 (CLAUDE.md §1).
    /// </summary>
    public const string LogFileName = "error.log";

    /// <summary>
    /// <see cref="RecoveryWindow"/> 안에서 살려 주는 횟수. 넘으면 폭주로 보고 포기한다.
    /// <para>
    /// 수치가 문서와 코드 두 곳에 있으면 갈린다 — 여기가 코드 쪽 정본이고
    /// docs/PRD-v2.md §14 가 그 근거다 (<c>PerformanceLog.Budget</c> 과 같은 자리).
    /// </para>
    /// </summary>
    public const int MaxRecoveries = 5;

    /// <summary>
    /// 폭주를 재는 창. <b>프로세스의 평생 예산이 아니다</b> — 상주 앱은 며칠씩 켜져 있고,
    /// 아침의 사고가 저녁의 한 번을 죽이면 안 된다.
    /// </summary>
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromMinutes(1);

    private readonly TimeProvider time;

    /// <summary>
    /// 살려 준 시각들. <b>마지막 시각 하나가 아니라 전부</b>를 담는 이유: "마지막
    /// 사고로부터 1분" 으로 재면 59초마다 한 번씩 터지는 폭주가 영원히 살아난다.
    /// 담기는 것은 최대 <see cref="MaxRecoveries"/> 개다.
    /// </summary>
    private readonly Queue<DateTimeOffset> recoveries = new(MaxRecoveries);

    private bool isVisible;
    private string? message;

    /// <param name="timeProvider">
    /// 한도 판정의 시계. 선택 주입이다 (<c>PaneViewModel</c> 의 type-ahead 와 같은 패턴) —
    /// 실제 시계로는 창 경계를 결정적으로 채점할 수 없다.
    /// </param>
    public CrashNoticeViewModel(TimeProvider? timeProvider = null)
        => time = timeProvider ?? TimeProvider.System;

    /// <summary>알림 바가 보이는가. 사고가 있었고 아직 접지 않았을 때만 참이다.</summary>
    public bool IsVisible
    {
        get => isVisible;
        private set => SetProperty(ref isVisible, value);
    }

    /// <summary>바에 적히는 한 줄. 접혀 있으면 <see langword="null"/> 이다.</summary>
    public string? Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    /// <summary>
    /// 사고 하나를 알린다. <b>돌려주는 값이 "삼켜도 되는가" 다</b> —
    /// <c>Host/Program</c> 이 그것을 <c>DispatcherUnhandledException</c> 의
    /// <c>Handled</c> 에 그대로 건다.
    /// <para>
    /// <b>판정이 여기 있는 이유</b>: <c>Program.cs</c> 는 TDD 가드의 검사 대상이 아니고
    /// 스스로 "판단은 여기 두지 않는다" 고 적고 있다. 거기가 하는 일은 순서를 정하는
    /// 것뿐이다.
    /// </para>
    /// <para>
    /// <b>UI 스레드에서만 불린다</b> — 부르는 곳이 dispatcher 예외 핸들러 하나다.
    /// 그래서 큐에 잠금이 없고, 속성을 마샬링 없이 그대로 민다.
    /// </para>
    /// </summary>
    public bool Report(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var now = time.GetUtcNow();

        // 창 밖으로 나간 것은 잊는다. 이것을 빼면 살려 준 횟수가 프로세스 평생 누적이 되어
        // 오래 켜 둘수록 죽기 쉬워진다 — 상주 앱에서는 그게 곧 "며칠 못 간다" 다.
        while (recoveries.TryPeek(out var at) && now - at > RecoveryWindow)
        {
            recoveries.Dequeue();
        }

        if (recoveries.Count >= MaxRecoveries)
        {
            // 포기한다. 바를 건드리지 않는 것이 의도다 — 곧 프로세스가 사라지므로 새 문구를
            // 볼 사람이 없고, 마지막으로 보인 것이 남는 편이 낫다.
            return false;
        }

        recoveries.Enqueue(now);

        // 예외 이름까지 드러낸다 (사용자 결정 2026-08-11). 익숙한 이름이면 그 한 줄로 다음
        // 행동이 갈리고, 아니면 로그를 연다. 메시지 전문은 넣지 않는다 — 길이가 예외마다
        // 달라 바 높이가 목록을 밀어낸다.
        Message = $"문제가 생겼지만 계속 쓸 수 있습니다 — {error.GetType().Name} ({LogFileName})";
        IsVisible = true;

        return true;
    }

    /// <summary>
    /// 바를 접는다. <b>"봤다" 이지 "그만 알려라" 가 아니다</b> — 다음 사고는 다시 뜬다.
    /// <para>
    /// 살려 준 횟수는 되돌리지 않는다. 접기가 한도를 채워 주면 폭주 중에 바를 닫는 것만으로
    /// 좀비가 되고, 하필 폭주 중에는 바가 계속 떠서 사용자가 계속 닫게 된다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        Message = null;
        IsVisible = false;
    }
}
