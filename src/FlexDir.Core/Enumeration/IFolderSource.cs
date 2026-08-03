using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Core.Enumeration;

/// <summary>
/// 폴더를 읽는 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에 둔다 —
/// <c>FindFirstFileEx</c> 기반이고 수동 검증 대상이다 (ADR-009).
/// <para>
/// 모든 호출은 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3). 열거가 동기라면 대용량·
/// 네트워크 폴더에서 목록이 다 차기 전까지 아무것도 보여줄 수 없다. 그래서 항목은
/// 비동기 스트림으로 점진적으로 흐르고, 매 항목마다 취소를 관측한다.
/// </para>
/// </summary>
public interface IFolderSource
{
    /// <summary>
    /// 폴더의 항목을 비동기 스트림으로 낸다. 열 수 없으면
    /// <see cref="LocationAccessException"/>. 스트림 도중에도 실패할 수 있다
    /// (열거 중 폴더가 사라지는 경우).
    /// <para>
    /// 오류를 결과 스트림에 항목으로 섞지 않는다 — 소비자가 항목마다 오류 여부를
    /// 검사해야 하면 모든 호출부가 오염된다.
    /// </para>
    /// </summary>
    IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct);

    /// <summary>
    /// 단건 조회. 없으면 null (예외가 아니다) — 감시 알림과 실제 상태가 어긋나는 것은
    /// 정상 상황이다(진실원천은 파일시스템 — CLAUDE.md §4). 외부 변경 알림을 받은 뒤
    /// 그 항목만 다시 확인하는 데 쓴다.
    /// <para>
    /// 권한 문제는 여기서도 <see cref="LocationAccessException"/> 이다 —
    /// 없음과 구분해야 사용자에게 할 말이 갈린다.
    /// </para>
    /// </summary>
    Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct);
}
