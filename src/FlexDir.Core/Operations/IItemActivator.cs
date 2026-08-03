using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

namespace FlexDir.Core.Operations;

/// <summary>
/// 항목 실행 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에 둔다 —
/// <c>ShellExecuteEx</c> 기반이고 수동 검증 대상이다 (ADR-009).
/// </summary>
public interface IItemActivator
{
    /// <summary>
    /// 더블클릭·Enter. <b>폴더가 아닌</b> 항목의 연결 프로그램을 실행한다.
    /// <para>
    /// 폴더 진입을 여기에 맡기지 않는다 — shell 은 폴더를 받으면 새 탐색기 창을 띄운다.
    /// 폴더는 호출자가 자기 페인에서 연다.
    /// </para>
    /// <para>
    /// 클라우드 자리표시자(<c>FileItem.IsContentAccessRisky</c>)도 그대로 실행한다.
    /// 다운로드를 트리거하는 것은 사용자가 의도한 행위다 — 접근을 피해야 하는 것은
    /// 썸네일뿐이다 (docs/SHELL_NOTES.md §열거 함정 3).
    /// </para>
    /// <para>
    /// 실행할 수 없으면 <see cref="LocationAccessException"/>. 연결 프로그램이 뜬 뒤의
    /// 수명은 우리 것이 아니므로 기다리지 않는다.
    /// </para>
    /// </summary>
    Task ActivateAsync(LocationId item, CancellationToken ct);
}
