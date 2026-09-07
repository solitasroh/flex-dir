using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

namespace FlexDir.Core.Tools;

/// <summary>
/// 외부 도구를 띄우는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
/// <para>
/// <b>계약 1 — 실패를 던지지 않고 <see cref="LocationErrorKind"/> 로 낸다.</b> 실패는
/// 정상 상황이고 (프로그램을 지웠을 수 있다) 호출자는 그것을 상태표시줄 한 줄로 만들어야
/// 한다 (<see cref="ExternalToolMessages.Describe"/>). 대화상자를 띄우는 길은 없다
/// (docs/UI_GUIDE.md §금지 목록 — 모달로 진행·오류를 알리지 않는다).
/// 성공은 <see cref="LocationErrorKind.None"/> 이다.
/// </para>
/// <para>
/// <b>계약 2 — 띄우기만 하고 기다리지 않는다.</b> 종료를 기다리면 그 프로그램이 닫힐
/// 때까지 호출이 묶인다. <c>arguments</c> 는 이미 치환이 끝난 문자열이며
/// (<see cref="ExternalToolCommand.FormatArguments"/>) 실행기가 손대지 않는다.
/// </para>
/// <para>
/// <b>계약 3 — <c>workingFolder</c> 는 작업 디렉터리로만 쓴다.</b> 인자에 자동으로
/// 덧붙이지 않는다 — 무엇을 인자로 줄지는 호출자가 이미 정했고, 덧붙이면 인자를
/// 비워 둔 프리셋(<c>cmd.exe</c> 등)에 경로가 하나 더 붙어 엉뚱한 것이 열린다.
/// </para>
/// <para>
/// <b>계약 4 — UI 스레드에서 부를 수 없다</b> (CLAUDE.md §3). 프로세스 생성은 실행
/// 파일이 네트워크 경로에 있으면 초 단위로 블로킹된다.
/// </para>
/// </summary>
public interface IExternalToolLauncher
{
    /// <summary>
    /// <paramref name="executable"/> 을 <paramref name="workingFolder"/> 를 작업
    /// 디렉터리로 삼아 띄운다. 성공은 <see cref="LocationErrorKind.None"/>,
    /// 실패는 그 사유다 — <b>던지지 않는다.</b>
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됐을 때. 이것만 나온다.</exception>
    ValueTask<LocationErrorKind> LaunchAsync(
        string executable,
        string arguments,
        LocationId workingFolder,
        CancellationToken ct);
}
