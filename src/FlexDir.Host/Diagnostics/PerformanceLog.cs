using System.Globalization;
using System.IO;

namespace FlexDir.Host.Diagnostics;

/// <summary>
/// 재는 자리 셋 (docs/ARCHITECTURE.md §7). 별도 벤치마크 CLI 를 만들지 않는 이유가 이
/// 목록이다 — 전작은 도구를 만들고 실사용과 무관한 것을 쟀다.
/// </summary>
public enum MeasurementPoint
{
    /// <summary>프로세스가 만들어진 순간부터 조립이 끝난 순간까지. 상주 프로세스가 한 번만 내는 값이다.</summary>
    ColdStart,

    /// <summary>상주 중 활성화 요청부터 창이 보일 때까지. 창이 생기는 phase B 에서 기록한다.</summary>
    WindowShown,

    /// <summary>폴더 전환부터 첫 항목이 목록에 붙을 때까지. 창이 생기는 phase B 에서 기록한다.</summary>
    FirstItem,
}

/// <summary>
/// 계측 수치를 <c>%LOCALAPPDATA%\flex-dir\perf.log</c> 에 한 줄씩 덧붙인다.
/// <para>
/// 앱 안에서 재고 앱 밖으로 내보내지 않는다. 읽는 방법은 파일을 여는 것 하나다 —
/// 재기 위한 별도 실행 파일이 금지 대상이다 (docs/ARCHITECTURE.md §7 · ADR-015).
/// </para>
/// <para>
/// 줄 모양: <c>시각 · 지점 · 걸린 ms · 예산 ms · ok|over</c> (탭 구분). 예산을 함께 적는
/// 이유는 나중에 보는 사람이 <c>docs/PRD.md</c> §5 를 찾아보지 않아도 판정이 서게 하려는
/// 것이다. 수치는 전부 잠정이므로 예산이 바뀌면 옛 줄과 새 줄이 갈리는 것도 보여야 한다.
/// </para>
/// </summary>
public sealed class PerformanceLog
{
    public const string FileName = "perf.log";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    /// <summary>
    /// 덧붙이기를 직렬화한다. 겹쳐 부르면 같은 파일을 두 곳에서 열어 공유 위반이 나는데,
    /// 아래 <c>catch</c> 가 그것을 삼키므로 <b>줄이 조용히 사라진다</b> — 나중에 수치를
    /// 보는 사람은 "그때 안 쟀나 보다" 로 읽는다.
    /// <para>
    /// <c>JsonViewStateStore</c> 의 <c>writeGate</c> 와 같은 이유·같은 수다.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim writeGate = new(1, 1);

    private readonly string directory;
    private readonly string path;

    public PerformanceLog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
        path = Path.Combine(directory, FileName);
    }

    /// <summary>
    /// 지점별 잠정 목표 (docs/PRD.md §5). 여기가 코드 쪽 정본이다 — 수치를 호출부마다
    /// 적으면 갈린다.
    /// </summary>
    public static TimeSpan Budget(MeasurementPoint point) => point switch
    {
        MeasurementPoint.ColdStart => TimeSpan.FromSeconds(1.5),
        MeasurementPoint.WindowShown => TimeSpan.FromMilliseconds(100),
        MeasurementPoint.FirstItem => TimeSpan.FromMilliseconds(150),
        _ => throw new ArgumentOutOfRangeException(nameof(point), point, "알 수 없는 계측 지점이다."),
    };

    /// <summary>
    /// 한 번의 측정을 남긴다. <c>IUsageLog</c> 와 달리 하루 한 줄로 접지 않는다 —
    /// 여기서 보는 것은 날짜가 아니라 매번의 수치다.
    /// <para>
    /// <b>실패해도 던지지 않는다.</b> 계측 때문에 앱이 뜨지 않으면 안 된다.
    /// </para>
    /// </summary>
    public async ValueTask RecordAsync(
        MeasurementPoint point,
        TimeSpan elapsed,
        DateTimeOffset at,
        CancellationToken ct)
    {
        // 음수는 시계가 뒤로 갔거나 시작 시각을 잘못 읽었다는 뜻이다. 남기면 그 수치를
        // 나중에 사람이 믿는다.
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero, nameof(elapsed));

        var budget = Budget(point);

        var line = string.Join(
            '\t',
            at.ToString(TimeFormat, CultureInfo.InvariantCulture),
            point.ToString(),
            ((long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            ((long)budget.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            elapsed <= budget ? "ok" : "over");

        await writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(directory);

            await File
                .AppendAllTextAsync(path, line + Environment.NewLine, ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 삼킨다. 위 문단의 이유다.
        }
        finally
        {
            writeGate.Release();
        }
    }
}
