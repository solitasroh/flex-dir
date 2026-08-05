using System.Globalization;
using System.IO;

using FlexDir.Shell.Usage;

using Xunit;

namespace FlexDir.Shell.Tests.Usage;

/// <summary>
/// 사용 기록 파일. 도그푸딩 게이트(ADR-007)가 스크립트로 읽을 대상이므로
/// <b>파일의 모양 자체가 계약</b>이다 — 한 줄에 한 날, 앞 10자가 <c>yyyy-MM-dd</c>.
/// <para>
/// 전부 <see cref="Path.GetTempPath"/> 아래에서 돈다. 실제
/// <c>%LOCALAPPDATA%\flex-dir\</c> 를 건드리면 게이트가 자기 테스트 실행을 사용으로 센다.
/// </para>
/// </summary>
public class FileUsageLogTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-usage-tests", Guid.NewGuid().ToString("N"));

    private static DateTimeOffset Day(int day, int hour = 9)
        => new(2026, 8, day, hour, 0, 0, TimeSpan.FromHours(9));

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

    [Fact]
    public async Task RecordAsync_CreatesTheDirectoryAndOneLine()
    {
        var log = new FileUsageLog(root);

        await log.RecordAsync(Day(5), CancellationToken.None);

        Assert.Equal("2026-08-05T09:00:00+09:00", Assert.Single(Lines()));
    }

    [Fact]
    public async Task RecordAsync_SameDayTwice_KeepsOneLine()
    {
        // 활성화마다 한 줄이면 파일이 사용량이 아니라 실행 횟수를 담는다. 게이트가 세는
        // 것은 날짜다.
        var log = new FileUsageLog(root);

        await log.RecordAsync(Day(5, 9), CancellationToken.None);
        await log.RecordAsync(Day(5, 21), CancellationToken.None);

        Assert.Single(Lines());
    }

    [Fact]
    public async Task RecordAsync_AnotherDay_Appends()
    {
        var log = new FileUsageLog(root);

        await log.RecordAsync(Day(5), CancellationToken.None);
        await log.RecordAsync(Day(6), CancellationToken.None);

        Assert.Equal(["2026-08-05", "2026-08-06"], Dates());
    }

    [Fact]
    public async Task RecordAsync_FromANewInstance_StillSeesTheDayAlreadyRecorded()
    {
        // 상주 프로세스라도 재부팅하면 새 인스턴스다. 메모리에만 들고 있으면 하루에 여러
        // 줄이 쌓인다.
        await new FileUsageLog(root).RecordAsync(Day(5, 9), CancellationToken.None);
        await new FileUsageLog(root).RecordAsync(Day(5, 21), CancellationToken.None);

        Assert.Single(Lines());
    }

    [Fact]
    public async Task RecordAsync_AfterACorruptLastLine_StillAppends()
    {
        // 손상된 파일 때문에 기록이 멈추면 게이트의 입력이 조용히 사라진다.
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, FileUsageLog.FileName), "쓰레기\n");

        await new FileUsageLog(root).RecordAsync(Day(5), CancellationToken.None);

        Assert.Equal(["쓰레기", "2026-08-05T09:00:00+09:00"], Lines());
    }

    [Fact]
    public async Task RecordAsync_WhenTheDirectoryCannotBeMade_DoesNotThrow()
    {
        // 디스크가 말을 듣지 않는다고 앱이 뜨지 않으면 도그푸딩 자체가 불가능하다.
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "blocked");
        await File.WriteAllTextAsync(blocked, "이 자리는 파일이다");

        var log = new FileUsageLog(blocked);

        await log.RecordAsync(Day(5), CancellationToken.None);

        Assert.True(File.Exists(blocked));
    }

    [Fact]
    public async Task RecordAsync_Cancelled_Throws()
    {
        var log = new FileUsageLog(root);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await log.RecordAsync(Day(5), cts.Token));
    }

    [Fact]
    public async Task Lines_AreWhatTheGateCounts()
    {
        // 게이트는 앞 10자를 날짜로 읽는다. 그 약속이 깨지면 스크립트가 조용히 0 을 센다.
        var log = new FileUsageLog(root);

        await log.RecordAsync(Day(5), CancellationToken.None);
        await log.RecordAsync(Day(7), CancellationToken.None);

        var days = Lines()
            .Select(line => DateOnly.ParseExact(line[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Distinct()
            .Count();

        Assert.Equal(2, days);
    }

    [Fact]
    public void Ctor_MissingDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileUsageLog(null!));
        Assert.Throws<ArgumentException>(() => new FileUsageLog("  "));
    }

    private string[] Lines()
    {
        var path = Path.Combine(root, FileUsageLog.FileName);

        return File.Exists(path)
            ? File.ReadAllLines(path)
            : [];
    }

    private string[] Dates() => [.. Lines().Select(line => line[..10])];
}
