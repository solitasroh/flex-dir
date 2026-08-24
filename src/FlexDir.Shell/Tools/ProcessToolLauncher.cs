using System.ComponentModel;
using System.Diagnostics;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Tools;

namespace FlexDir.Shell.Tools;

/// <summary>
/// 외부 도구를 <see cref="Process"/> 로 띄우는 <see cref="IExternalToolLauncher"/> 구현체.
///
/// <para>
/// <b>COM 이 아니다</b> — <see cref="AppPathsToolCatalog"/> 와 같은 자리다. STA 워커도
/// 정리도 필요 없어 <see cref="IDisposable"/> 을 구현하지 않고 <c>AppComposition</c> 의
/// 정리 목록에도 들어가지 않는다. <b>아파트먼트와 블로킹은 다른 문제이며</b>
/// (<c>.harness/HANDOFF.md</c> §규칙 8) 여기 남는 것은 블로킹뿐이라 <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>
/// 하나로 답한다 — 실행 파일이 네트워크 경로에 있으면 프로세스 생성이 초 단위로 막힌다
/// (CLAUDE.md §3).
/// </para>
///
/// <para>
/// <b>띄우고 끝이다.</b> <c>WaitForExit</c> 를 부르지 않는다 (포트 계약 2) — 부르면 그
/// 터미널이 닫힐 때까지 호출이 묶인다. 프로그램의 수명은 우리 것이 아니므로 받은
/// <see cref="Process"/> 는 곧바로 놓는다 (<see cref="Activation.ShellItemActivator"/> 가
/// 프로세스 핸들을 아예 요청하지 않는 것과 같은 판단이다).
/// </para>
///
/// <para>
/// <b>인자를 손대지 않는다.</b> <c>arguments</c> 는 이미 치환이 끝난 한 줄이고
/// (<see cref="ExternalToolCommand.FormatArguments"/>) 그대로 <see cref="ProcessStartInfo.Arguments"/>
/// 에 싣는다. <see cref="ProcessStartInfo.ArgumentList"/> 를 쓰면 사용자가 적은 한 줄을
/// 우리가 쪼개게 되고 따옴표 규칙이 사용자 기대와 갈린다.
/// </para>
///
/// <para>
/// <b>실패를 던지지 않는다</b> (포트 계약 1). 매핑은 <see cref="Win32ErrorMapping"/> 하나뿐이며
/// 여기에 두 번째 표를 만들지 않는다 — 표가 둘이면 같은 코드가 두 뜻이 된다.
/// </para>
/// </summary>
public sealed class ProcessToolLauncher : IExternalToolLauncher
{
    private readonly Action<ProcessStartInfo> start;

    public ProcessToolLauncher() => start = Start;

    /// <summary>
    /// 실행 지점을 바꿔 끼운다. 실물은 성공하면 창을 띄우므로 — 게이트가 돌 때마다
    /// 터미널이 뜨는 것은 자동 테스트가 해서는 안 되는 일이다
    /// (<c>.harness/HANDOFF.md</c> §규칙 5) — 재는 것은 <b>무엇을 실어 보내는가</b> 와
    /// <b>실패를 어떻게 접는가</b> 뿐이다.
    /// <para>
    /// <see cref="Activation.ShellItemActivator"/> 는 같은 자리를 오류 코드를 내는
    /// <c>Func</c> 로 열었다. 여기가 <see cref="Action{T}"/> 인 것은 실패가 코드가 아니라
    /// <b>예외</b>로 오기 때문이다 — 테스트가 던져야 그 경로를 밟는다.
    /// </para>
    /// </summary>
    internal ProcessToolLauncher(Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        this.start = start;
    }

    public async ValueTask<LocationErrorKind> LaunchAsync(
        string executable,
        string arguments,
        LocationId workingFolder,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ct.ThrowIfCancellationRequested();

        // 실행 파일이 비어 있는 것은 정상 상태다 — Custom 프리셋을 고르고 아직 경로를 안
        // 적은 중간이 그렇다 (docs/PRD-v2.md §19). 프로세스를 만들려 들면 예외 문자열이
        // 상태표시줄에 뜬다. 그것은 사용자에게 "없다" 이지 "알 수 없다" 가 아니다.
        if (string.IsNullOrWhiteSpace(executable))
        {
            return LocationErrorKind.NotFound;
        }

        return await Task.Run(() => Launch(executable, arguments, workingFolder), ct).ConfigureAwait(false);
    }

    private LocationErrorKind Launch(string executable, string arguments, LocationId workingFolder)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,

            // \\?\ 확장 접두사가 붙은 경로를 작업 디렉터리로 받는 프로그램은 거의 없다.
            // 다른 구현체와 같이 DisplayPath 를 준다 (Value 가 아니다).
            WorkingDirectory = workingFolder.DisplayPath,

            // true 면 WorkingDirectory 가 무시되는 경우가 있고, 실패가 Win32Exception 이
            // 아니라 shell 대화상자로 나온다 — 모달 금지 위반이자 --blame-hang 감이다.
            UseShellExecute = false,
        };

        try
        {
            start(info);

            return LocationErrorKind.None;
        }
        catch (OperationCanceledException)
        {
            // 포트 계약 1 의 유일한 예외다. 접으면 취소가 오류 문구가 된다.
            throw;
        }
        catch (Win32Exception ex)
        {
            var kind = Win32ErrorMapping.Classify(ex.NativeErrorCode);

            // Classify 는 0 과 모르는 코드에 None 을 내는데 이 포트에서 None 은 성공이다.
            // 그대로 내보내면 아무것도 안 뜬 채 상태표시줄도 조용하다
            // (ShellItemActivator.Activate 와 같은 자리).
            return kind == LocationErrorKind.None ? LocationErrorKind.Unknown : kind;
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                      or DirectoryNotFoundException
                                      or InvalidOperationException
                                      or PlatformNotSupportedException)
        {
            // 넷 다 "띄울 것이 거기 없다" 다 — 프로그램을 지운 뒤 설정에 이름만 남은
            // 상태가 정상이므로 이것은 오류 화면이 아니라 한 줄 사유가 된다.
            return LocationErrorKind.NotFound;
        }
        catch (Exception)
        {
            // 실행 실패의 예외 공간은 위보다 넓다. 예외 문자열을 상태표시줄에 흘리지 않는다.
            return LocationErrorKind.Unknown;
        }
    }

    /// <summary>
    /// 실제 실행. <b>반환된 프로세스를 곧바로 놓는다</b> — 쥐고 있으면 그 핸들이 샌다.
    /// </summary>
    private static void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();
}
