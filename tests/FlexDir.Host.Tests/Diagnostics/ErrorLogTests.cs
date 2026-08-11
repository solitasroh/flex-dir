using System.IO;

using FlexDir.App.ViewModels;

using FlexDir.Host.Diagnostics;

using Xunit;

namespace FlexDir.Host.Tests.Diagnostics;

/// <summary>
/// 삼킨 예외가 남는 자리 (docs/PRD-v2.md §14).
/// <para>
/// <b>이 파일이 없으면 정보가 준다.</b> 지금까지 UI 스레드 예외를 볼 수 있었던 이유는
/// 처리되지 않은 예외가 Windows 이벤트 로그에 관리 스택을 남기기 때문이다 —
/// <c>Handled=true</c> 로 삼키는 순간 그 항목이 생기지 않는다. 살리는 것과 남기는 것은
/// 함께 와야 한다.
/// </para>
/// <para>
/// <see cref="PerformanceLog"/> 와 같은 자리·같은 모양이되 <b>동기</b>다. 이유는
/// <see cref="ErrorLog.Record"/> 의 주석에 있다.
/// </para>
/// </summary>
public class ErrorLogTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 8, 11, 14, 23, 11, TimeSpan.FromHours(9));

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-error-tests", Guid.NewGuid().ToString("N"));

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

    private string Text() => File.ReadAllText(Path.Combine(root, ErrorLog.FileName));

    [Fact]
    public void Record_WritesTheTimeTheVerdictAndTheException()
    {
        var log = new ErrorLog(root);

        log.Record(Failed("우클릭 한 번에 죽었다"), recovered: true, At);

        var text = Text();

        Assert.StartsWith("2026-08-11T14:23:11+09:00\trecovered\t", text, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.Contains("우클릭 한 번에 죽었다", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_KeepsTheStackTrace()
    {
        // 남기는 이유가 이것이다. 타입과 메시지만으로는 2026-08-10 의 크래시를 찾지
        // 못했다 — 스택이 ContextMenuService 를 가리켜 리소스 순서로 갈 수 있었다.
        var log = new ErrorLog(root);

        log.Record(Failed("x"), recovered: true, At);

        Assert.Contains(nameof(Failed), Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_WhenItGaveUp_MarksTheLineDifferently()
    {
        // 마지막 줄이 프로세스가 왜 사라졌는지를 말한다. 삼킨 것과 구분되지 않으면
        // 로그가 "살아 있었다" 로만 읽힌다.
        var log = new ErrorLog(root);

        log.Record(Failed("x"), recovered: false, At);

        Assert.StartsWith("2026-08-11T14:23:11+09:00\tfatal\t", Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ManyTimes_Appends()
    {
        var log = new ErrorLog(root);

        log.Record(Failed("첫 번째"), recovered: true, At);
        log.Record(Failed("두 번째"), recovered: true, At);

        var text = Text();

        Assert.Contains("첫 번째", text, StringComparison.Ordinal);
        Assert.Contains("두 번째", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_SeparatesEntriesWithABlankLine()
    {
        // 스택이 여러 줄이라 구분이 없으면 어디서 한 항목이 끝나는지 눈으로 못 가른다.
        var log = new ErrorLog(root);

        log.Record(Failed("첫 번째"), recovered: true, At);
        log.Record(Failed("두 번째"), recovered: true, At);

        Assert.Equal(2, Text().Split(Environment.NewLine + Environment.NewLine).Length - 1);
    }

    [Fact]
    public void Record_WhenTheDirectoryIsMissing_MakesIt()
    {
        // 첫 사고가 상태 폴더보다 먼저 올 수 있다.
        Assert.False(Directory.Exists(root));

        new ErrorLog(root).Record(Failed("x"), recovered: true, At);

        Assert.True(File.Exists(Path.Combine(root, ErrorLog.FileName)));
    }

    [Fact]
    public void Record_WhenTheDirectoryCannotBeMade_DoesNotThrow()
    {
        // 여기서 던지면 예외 핸들러 안에서 예외가 나는 것이고, 그러면 살리려던 그
        // 프로세스를 로그가 죽인다.
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "blocked");
        File.WriteAllText(blocked, "이 자리는 파일이다");

        new ErrorLog(blocked).Record(Failed("x"), recovered: true, At);

        Assert.True(File.Exists(blocked));
    }

    [Fact]
    public void Record_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ErrorLog(root).Record(null!, true, At));

    [Fact]
    public void FileName_IsWhatTheCrashNoticePointsAt()
    {
        // 문구는 App 에 있고 파일은 Host 가 쓴다 (참조 방향은 Host → App 이라 App 이
        // 이 상수를 볼 수 없다). 둘이 갈리면 사용자가 없는 파일을 찾는다.
        Assert.Equal(CrashNoticeViewModel.LogFileName, ErrorLog.FileName);
    }

    /// <summary>실제로 던져 스택을 채운다 — <c>new</c> 만 한 예외에는 스택이 없다.</summary>
    private static Exception Failed(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }
}
