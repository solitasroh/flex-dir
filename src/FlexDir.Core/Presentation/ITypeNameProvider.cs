namespace FlexDir.Core.Presentation;

/// <summary>
/// "유형" 컬럼 문자열을 내는 포트 (docs/DESIGN.md §3). 문구는 Windows Shell 에서 오므로
/// 구현체는 <c>FlexDir.Shell</c> 에 둔다 — 로케일과 설치된 프로그램에 따라 달라지는 값이고
/// 수동 검증 대상이다 (ADR-009).
/// <para>
/// 비동기인 이유: shell 조회는 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3).
/// </para>
/// <para>
/// 구현체는 확장자마다 <b>한 번만</b> 조회해 캐시한다 (docs/SHELL_NOTES.md §아이콘 —
/// "파일마다 아이콘을 조회하지 마라. 확장자마다 한 번만"). 호출자도 같은 확장자를 두 번
/// 묻지 않는다 — 10만 항목 폴더에서 확장자가 열 종류면 조회도 열 번이어야 한다.
/// </para>
/// </summary>
public interface ITypeNameProvider
{
    /// <summary>
    /// 확장자에 대응하는 표시용 유형 이름 (예: "텍스트 문서").
    /// <paramref name="extension"/> 은 점을 포함하지 않는 소문자다 (<c>FileItem.Extension</c>).
    /// 빈 문자열은 확장자 없음이며, 디렉터리도 빈 문자열로 온다 —
    /// <paramref name="isDirectory"/> 로 갈라야 "파일 폴더" 와 확장자 없는 파일이 구분된다.
    /// </summary>
    ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct);
}
