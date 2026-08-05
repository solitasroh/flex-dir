using FlexDir.Host.Diagnostics;

namespace FlexDir.Host.Startup;

/// <summary>
/// 창 표시에 <see cref="MeasurementPoint.WindowShown"/> 계측을 두른다 — 활성화가 창
/// 표시를 맡긴 순간부터 창이 보일 때까지다 (docs/ARCHITECTURE.md §7 · docs/PRD.md §5).
/// <para>
/// <c>Program</c> 의 표시 람다에 직접 재지 않는 이유: <c>Program.cs</c> 는 TDD 가드의
/// 검사 대상이 아니라서 판단이 채점되지 않는 자리에 들어간다.
/// 라우터의 <c>presentWindow</c> 자리에 <see cref="PresentAsync"/> 를 끼운다.
/// </para>
/// </summary>
public sealed class WindowPresenter
{
    private readonly Func<CancellationToken, Task> show;
    private readonly PerformanceLog log;
    private readonly TimeProvider clock;

    /// <param name="show">창을 실제로 보이고 앞으로 가져오는 손 (UI 스레드로 가는 통로).</param>
    public WindowPresenter(Func<CancellationToken, Task> show, PerformanceLog log, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);

        this.show = show;
        this.log = log;
        this.clock = clock;
    }

    /// <summary>
    /// 창을 보이고 걸린 시간을 남긴다. 매 활성화가 계측 대상이다 — 첫 실행만 재면 상주 중
    /// 두 번째 실행이 굼떠져도 수치에 보이지 않는다 (ADR-003 의 존재 이유가 그 경로다).
    /// <para>표시가 실패하면 남기지 않는다 — 뜨지 않은 창의 시간은 수치가 아니다.</para>
    /// </summary>
    public async Task PresentAsync(CancellationToken ct)
    {
        // 벽시계가 아니라 타임스탬프다. 표시 도중 시계가 조정되면 (NTP) 벽시계 차는
        // 음수가 될 수 있고, 그 값은 RecordAsync 가 거부한다.
        var start = clock.GetTimestamp();

        await show(ct).ConfigureAwait(false);

        await log
            .RecordAsync(MeasurementPoint.WindowShown, clock.GetElapsedTime(start), clock.GetLocalNow(), ct)
            .ConfigureAwait(false);
    }
}
