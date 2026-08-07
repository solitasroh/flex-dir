using System.IO;

using FlexDir.Host.Diagnostics;

using Xunit;

namespace FlexDir.Host.Tests.Diagnostics;

/// <summary>
/// 계측은 앱 안에 두고 실제 사용 경로에서 잰다 (docs/ARCHITECTURE.md §7 — 금지 대상은
/// <b>재는 도구</b>다). 예산은 docs/PRD.md §5 의 잠정 목표다.
/// <para>
/// 전부 <see cref="Path.GetTempPath"/> 아래에서 돈다. 실제
/// <c>%LOCALAPPDATA%\flex-dir\</c> 를 건드리면 게이트가 자기 테스트 실행을 계측으로 센다.
/// </para>
/// </summary>
public class PerformanceLogTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 8, 5, 9, 0, 0, TimeSpan.FromHours(9));

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-perf-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 정리 실패로 테스트를 실패로 만들지 않는다.
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(MeasurementPoint.ColdStart, 1500)]
    [InlineData(MeasurementPoint.WindowShown, 100)]
    [InlineData(MeasurementPoint.FirstItem, 150)]
    public void Budget_IsThePerformanceBudget(MeasurementPoint point, int milliseconds)
    {
        // 수치가 문서와 코드 두 곳에 있으면 갈린다. 여기가 코드 쪽 정본이고
        // docs/PRD.md §5 가 그 근거다.
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), PerformanceLog.Budget(point));
    }

    [Fact]
    public void Budget_UnknownPoint_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceLog.Budget((MeasurementPoint)99));

    [Fact]
    public async Task RecordAsync_WithinTheBudget_WritesOneOkLine()
    {
        var log = new PerformanceLog(root);

        await log.RecordAsync(MeasurementPoint.ColdStart, TimeSpan.FromMilliseconds(812), At, default);

        Assert.Equal("2026-08-05T09:00:00+09:00\tColdStart\t812\t1500\tok", Assert.Single(Lines()));
    }

    [Fact]
    public async Task RecordAsync_OverTheBudget_MarksTheLine()
    {
        // 넘긴 것이 눈에 띄지 않으면 계측 파일은 읽히지 않는다.
        var log = new PerformanceLog(root);

        await log.RecordAsync(MeasurementPoint.FirstItem, TimeSpan.FromMilliseconds(400), At, default);

        Assert.EndsWith("\tFirstItem\t400\t150\tover", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_ManyTimes_Appends()
    {
        // 사용 기록과 달리 하루 한 줄로 접지 않는다 — 재는 것은 날짜가 아니라 매번의 수치다.
        var log = new PerformanceLog(root);

        await log.RecordAsync(MeasurementPoint.ColdStart, TimeSpan.FromMilliseconds(1), At, default);
        await log.RecordAsync(MeasurementPoint.ColdStart, TimeSpan.FromMilliseconds(2), At, default);

        Assert.Equal(2, Lines().Length);
    }

    // 겹쳐 부르면 같은 파일을 두 곳에서 연다. 공유 위반은 예외인데 RecordAsync 는 실패를
    // 삼키므로(계측 때문에 앱이 죽으면 안 되니까) 줄이 조용히 사라진다 — 나중에 수치를
    // 보는 사람은 그것을 "그때 안 쟀나 보다" 로 읽는다.
    [Fact]
    public async Task RecordAsync_CalledConcurrently_LosesNothing()
    {
        var log = new PerformanceLog(root);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            log.RecordAsync(MeasurementPoint.FirstItem, TimeSpan.FromMilliseconds(index), At, default).AsTask()));

        Assert.Equal(20, Lines().Length);
    }

    [Fact]
    public async Task RecordAsync_WhenTheDirectoryCannotBeMade_DoesNotThrow()
    {
        // 계측 때문에 앱이 뜨지 않으면 안 된다.
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "blocked");
        await File.WriteAllTextAsync(blocked, "이 자리는 파일이다");

        var log = new PerformanceLog(blocked);

        await log.RecordAsync(MeasurementPoint.ColdStart, TimeSpan.Zero, At, default);

        Assert.True(File.Exists(blocked));
    }

    [Fact]
    public async Task RecordAsync_NegativeElapsed_Throws()
    {
        // 음수는 시계가 뒤로 갔거나 시작 시각을 잘못 읽었다는 뜻이다. 파일에 남기면
        // 그 수치를 나중에 사람이 믿는다.
        var log = new PerformanceLog(root);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await log.RecordAsync(
                MeasurementPoint.ColdStart, TimeSpan.FromMilliseconds(-1), At, default));
    }

    [Fact]
    public void Ctor_MissingDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PerformanceLog(null!));
        Assert.Throws<ArgumentException>(() => new PerformanceLog("  "));
    }

    private string[] Lines()
    {
        var path = Path.Combine(root, PerformanceLog.FileName);

        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }
}
