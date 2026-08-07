using System.Globalization;

using FlexDir.Core.Usage;

namespace FlexDir.Shell.Usage;

/// <summary>
/// 사용 기록을 <c>%APPDATA%\flex-dir\usage.log</c> 에 한 줄씩 덧붙이는 구현체.
/// <para>
/// <b>파일의 모양이 계약이다.</b> 읽는 것이 우리 코드가 아니라서 그렇게 정했다 — 한 줄에
/// 하루, 앞 10자가 <c>yyyy-MM-dd</c>. JSON 이 아닌 이유가 그것이다:
/// <c>Get-Content | Select-String</c> 한 줄로 셀 수 있다.
/// 그 계약을 요구하던 게이트 스크립트는 폐기됐지만 (ADR-007 §폐기) 모양은 그대로 둔다 —
/// 이제 읽는 것이 사람이다.
/// </para>
/// <para>
/// <b>하루에 한 줄만 남긴다.</b> 활성화마다 남기면 파일이 사용량이 아니라 실행 횟수를 담고
/// 무한히 자란다. 세는 것은 날짜다 (<see cref="IUsageLog"/>).
/// </para>
/// </summary>
public sealed class FileUsageLog : IUsageLog
{
    public const string FileName = "usage.log";

    /// <summary>게이트가 앞 10자를 <c>yyyy-MM-dd</c> 로 읽는다. 오프셋까지 남겨 사람이 볼 수 있게 한다.</summary>
    private const string LineFormat = "yyyy-MM-ddTHH:mm:sszzz";

    private const string DateFormat = "yyyy-MM-dd";

    private readonly string directory;
    private readonly string path;

    // 기록은 실행과 활성화에서 오고 둘이 겹칠 수 있다. 겹치면 같은 날이 두 줄이 되거나
    // 반쯤 쓰인 줄이 남는다.
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>마지막으로 남긴 날. 아직 파일을 보지 않았으면 <c>null</c> 이다.</summary>
    private DateOnly? recorded;

    private bool read;

    public FileUsageLog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
        path = Path.Combine(directory, FileName);
    }

    public async ValueTask RecordAsync(DateTimeOffset at, CancellationToken ct)
    {
        // 취소는 그대로 낸다. 실패를 삼키는 것과 종료 요청을 무시하는 것은 다른 일이다.
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var day = DateOnly.FromDateTime(at.DateTime);

            if (!read)
            {
                // 상주 프로세스라도 재부팅하면 새 인스턴스다. 메모리만 보면 하루에 여러 줄이 쌓인다.
                recorded = await LastRecordedAsync(ct).ConfigureAwait(false);
                read = true;
            }

            if (recorded == day)
            {
                return;
            }

            await AppendAsync(at, ct).ConfigureAwait(false);

            recorded = day;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 파일에 남아 있는 마지막 날. 파일이 없거나 마지막 줄이 날짜가 아니면 <c>null</c> 이다 —
    /// 손상된 파일 때문에 기록이 멈추면 게이트의 입력이 조용히 사라진다.
    /// </summary>
    private async ValueTask<DateOnly?> LastRecordedAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
            var last = lines.LastOrDefault(line => line.Length >= DateFormat.Length);

            return last is not null
                && DateOnly.TryParseExact(
                    last[..DateFormat.Length], DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day)
                ? day
                : null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 읽지 못했으면 모르는 것으로 둔다. 그 결과 하루가 두 줄이 될 수 있지만,
            // 날짜를 세는 게이트에는 영향이 없다.
            return null;
        }
    }

    private async ValueTask AppendAsync(DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(directory);

            await File
                .AppendAllTextAsync(
                    path,
                    at.ToString(LineFormat, CultureInfo.InvariantCulture) + Environment.NewLine,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 던지지 않는다 (IUsageLog). 디스크가 말을 듣지 않는다고 앱이 뜨지 않으면
            // 도그푸딩 자체가 불가능해진다.
        }
    }
}
