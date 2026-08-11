using System.Globalization;
using System.IO;

namespace FlexDir.Host.Diagnostics;

/// <summary>
/// UI 스레드 예외를 <c>%APPDATA%\flex-dir\error.log</c> 에 남긴다 (docs/PRD-v2.md §14).
///
/// <para>
/// <b>삼키는 것과 짝이다.</b> 지금까지 UI 스레드 예외를 진단할 수 있었던 유일한 이유는
/// 처리되지 않은 예외가 Windows 이벤트 로그의 <c>.NET Runtime</c> 항목에 관리 스택을
/// 남기기 때문이었다 — <c>CrashNoticeViewModel</c> 이 <c>Handled</c> 를 걸면 그 항목이
/// 생기지 않는다. <b>살리기만 하고 남기지 않으면 정보가 준다.</b>
/// </para>
///
/// <para>
/// <see cref="PerformanceLog"/> 와 같은 자리·같은 모양이되 다른 것이 둘이다.
/// </para>
/// <list type="number">
///   <item>
///     <b>동기다.</b> 포기 판정(<c>fatal</c>)일 때는 이 줄을 쓴 직후 프로세스가 죽는다 —
///     비동기로 던져 두면 <b>정확히 가장 필요한 줄</b>이 유실된다. UI 스레드에서 저장소에
///     닿는 것이 되지만 (CLAUDE.md §3), 여기는 이미 사고 처리 경로이고 종료 경로가
///     <c>Workspace.PersistAsync</c> 를 같은 스레드에서 기다리는 것과 같은 예외다.
///   </item>
///   <item>
///     <b>쓰기 잠금이 없다.</b> 부르는 곳이 dispatcher 예외 핸들러 하나뿐이라 겹칠 상대가
///     없다. 다른 스레드에서도 부르게 되면 <see cref="PerformanceLog"/> 의 <c>writeGate</c>
///     가 먼저 와야 한다.
///   </item>
/// </list>
///
/// <para>
/// 항목 모양: 첫 줄이 <c>시각 · recovered|fatal · 예외 전문 첫 줄</c>(탭 구분)이고 그 뒤로
/// 스택이 이어진 다음 <b>빈 줄</b>로 끝난다. 스택이 여러 줄이라 구분이 없으면 어디서 한
/// 항목이 끝나는지 눈으로 가를 수 없다.
/// </para>
/// </summary>
public sealed class ErrorLog
{
    /// <summary>
    /// <c>CrashNoticeViewModel.LogFileName</c> 과 같아야 한다 — 알림 바가 사용자에게
    /// 가리키는 이름이 이것이다. 갈리지 않는 것은 <c>ErrorLogTests</c> 가 고정한다.
    /// </summary>
    public const string FileName = "error.log";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    private readonly string directory;
    private readonly string path;

    public ErrorLog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
        path = Path.Combine(directory, FileName);
    }

    /// <summary>
    /// 사고 하나를 남긴다.
    /// <para>
    /// <b>실패해도 던지지 않는다.</b> 여기서 던지면 예외 핸들러 안에서 예외가 나는 것이고,
    /// 그러면 살리려던 그 프로세스를 로그가 죽인다.
    /// </para>
    /// </summary>
    /// <param name="recovered">
    /// 삼키기로 했는가. 마지막 줄이 <c>fatal</c> 이면 프로세스가 왜 사라졌는지를 그것이
    /// 말한다 — 구분이 없으면 로그가 "그때도 살아 있었다" 로만 읽힌다.
    /// </param>
    public void Record(Exception error, bool recovered, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(error);

        // ToString() 첫 줄이 이미 "타입: 메시지" 다. 타입을 따로 적으면 같은 것이 두 번 남는다.
        var entry = string.Join(
            '\t',
            at.ToString(TimeFormat, CultureInfo.InvariantCulture),
            recovered ? "recovered" : "fatal",
            error.ToString());

        try
        {
            Directory.CreateDirectory(directory);

            File.AppendAllText(path, entry + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception)
        {
            // 삼킨다. 위 문단의 이유다.
        }
    }
}
