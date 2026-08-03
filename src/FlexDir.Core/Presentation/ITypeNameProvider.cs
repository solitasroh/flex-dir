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
/// <para>
/// <b>실패는 예외가 아니라 빈 문자열이다.</b> 유형 이름은 보조 텍스트고(docs/DESIGN.md §5 —
/// <c>--text-2</c>) 못 읽는 것은 정상이다. shell 의 폴백 체인도 끝에서 자리표시자로 물러난다
/// (docs/SHELL_NOTES.md §아이콘). 예외로 만들면 호출자가 <b>항목마다</b> 잡아야 한다 —
/// <see cref="IThumbnailSource"/> 가 실패를 <c>null</c> 로 내는 것과 같은 이유다.
/// 빈 문자열을 "확장자 없음" 과 구분할 필요는 없다: 어느 쪽이든 유형 컬럼이 빈칸이다.
/// </para>
/// <para>
/// 취소만 예외다 (<see cref="OperationCanceledException"/>). 실패와 달리 취소는 호출자가 건
/// 것이고, 조용히 빈 문자열로 돌아오면 이미 떠난 폴더의 줄을 계속 만들게 된다.
/// </para>
/// <para>
/// <b>그럼에도 호출자는 예외를 방어한다.</b> 구현체는 COM 위에 서므로 이 계약을 어기는 예외가
/// 나올 수 있고, 유형 이름 하나 때문에 폴더가 통째로 열리지 않는 것은 어느 쪽이 틀렸든 잘못된
/// 결과다 (<c>ThumbnailRequestScheduler</c> 가 형식 아이콘 실패를 삼키는 것과 같은 판단).
/// </para>
/// </summary>
public interface ITypeNameProvider
{
    /// <summary>
    /// 확장자에 대응하는 표시용 유형 이름 (예: "텍스트 문서").
    /// <paramref name="extension"/> 은 점을 포함하지 않는 소문자다 (<c>FileItem.Extension</c>).
    /// 빈 문자열은 확장자 없음이며, 디렉터리도 빈 문자열로 온다 —
    /// <paramref name="isDirectory"/> 로 갈라야 "파일 폴더" 와 확장자 없는 파일이 구분된다.
    /// <para>조회에 실패하면 빈 문자열이다. 예외를 던지지 않는다.</para>
    /// </summary>
    ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct);
}
