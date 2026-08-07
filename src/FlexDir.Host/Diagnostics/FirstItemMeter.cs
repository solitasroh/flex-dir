using System.Collections.Specialized;
using System.ComponentModel;

using FlexDir.App.ViewModels;

namespace FlexDir.Host.Diagnostics;

/// <summary>
/// 페인 하나를 관찰해 <see cref="MeasurementPoint.FirstItem"/> 을 잰다 — 폴더 전환부터
/// 첫 항목이 목록에 붙을 때까지다 (docs/ARCHITECTURE.md §7 · docs/PRD.md §5 · 예산 150ms).
/// <para>
/// 페인 안에서 재지 않는 이유: 계측 파일(<c>perf.log</c>)은 Host 의 것이고, App 이 그것을
/// 알면 참조 방향이 뒤집힌다 (docs/ARCHITECTURE.md §1). 페인이 이미 밖으로 내보내는 신호
/// 둘로 충분하다 — <c>CurrentLocation</c> 변경이 전환이고, 다음 목록 변경이 첫 도착이다.
/// </para>
/// <para>
/// 두 신호는 모두 UI 스레드(dispatcher)에서 온다. 그래서 잠금이 없다.
/// </para>
/// </summary>
public sealed class FirstItemMeter : IDisposable
{
    private readonly PaneViewModel pane;
    private readonly PerformanceLog log;
    private readonly TimeProvider clock;

    private long startedAt;
    private bool armed;
    private Task recording = Task.CompletedTask;

    public FirstItemMeter(PaneViewModel pane, PerformanceLog log, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);

        this.pane = pane;
        this.log = log;
        this.clock = clock;

        pane.PropertyChanged += OnPaneChanged;
        pane.Items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>
    /// 진행 중인 기록. 기록은 목록 변경 이벤트 안에서 시작되므로 기다릴 손이 이것뿐이다 —
    /// 테스트가 파일을 읽기 전에, 종료가 프로세스를 접기 전에 기다린다.
    /// </summary>
    public Task Recording => recording;

    public void Dispose()
    {
        pane.PropertyChanged -= OnPaneChanged;
        pane.Items.CollectionChanged -= OnItemsChanged;
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 같은 폴더 다시 읽기(새로 고침·감시 갱신)는 CurrentLocation 을 바꾸지 않으므로
        // 재지 않는다 — FirstItem 은 전환의 체감이지 갱신의 체감이 아니다.
        if (e.PropertyName == nameof(PaneViewModel.CurrentLocation))
        {
            armed = true;
            startedAt = clock.GetTimestamp();

            return;
        }

        // 열거가 첫 항목 없이 끝났다 (빈 폴더·열기 실패). 빈 목록을 빈 채로 두는 교체는
        // 목록 이벤트를 내지 않으므로, 여기서 접지 않으면 한참 뒤 감시 갱신이 붙인 항목이
        // "첫 항목" 으로 잡힌다.
        if (armed
            && e.PropertyName == nameof(PaneViewModel.Status)
            && pane.Status is PaneStatus.Empty or PaneStatus.Error)
        {
            armed = false;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!armed)
        {
            return;
        }

        // 전환 뒤 첫 목록 변경이 판정의 전부다 — 성공이면 첫 배치, 실패·빈 폴더면 비우기다.
        // 여기서 접지 않으면 한참 뒤 감시 갱신이 "첫 항목" 으로 잡힌다.
        armed = false;

        if (pane.Items.Count == 0)
        {
            // 첫 항목이 없다 (빈 폴더·열기 실패). 0ms 를 남기면 평균이 좋아 보이게 거짓말한다.
            return;
        }

        // 앞선 기록에 이어 붙인다. 덮어쓰면 Recording 이 마지막 하나만 가리키고, 앞선
        // 기록은 아직 파일을 쥔 채 남는다 — 종료 경로에서는 그 줄이 잘리고, 파일을 읽는
        // 쪽에서는 공유 위반이 된다. 간헐 실패로 나타나던 자리다.
        recording = RecordAfterAsync(recording, clock.GetElapsedTime(startedAt), clock.GetLocalNow());
    }

    /// <summary>
    /// 걸린 시간과 시각은 <b>이벤트 시점</b>의 값이다 — 앞선 기록을 기다린 뒤에 재면
    /// 그 대기 시간이 수치에 섞인다.
    /// </summary>
    private async Task RecordAfterAsync(Task previous, TimeSpan elapsed, DateTimeOffset at)
    {
        await previous.ConfigureAwait(false);

        await log
            .RecordAsync(MeasurementPoint.FirstItem, elapsed, at, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
