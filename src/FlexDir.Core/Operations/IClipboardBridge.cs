using FlexDir.Core.Locations;

namespace FlexDir.Core.Operations;

/// <summary>
/// 탐색기 호환 클립보드 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에
/// 둔다 — <c>CFSTR_SHELLIDLIST</c> + <c>Preferred DropEffect</c> 기반이고 수동 검증
/// 대상이다 (ADR-009).
/// <para>
/// 자체 포맷을 만들지 않는다. 탐색기에서 복사해 flex-dir 에서 붙여넣는 것이 성립해야
/// 한다 (docs/PRD.md §2) — 그 호환이 이 포트가 존재하는 이유다.
/// </para>
/// <para>
/// 비동기가 아니다. 클립보드 접근은 OLE 아파트먼트에 묶여 있어 호출 스레드를 고를 수
/// 없고, 옮기는 것은 항목의 <b>식별자</b>뿐이다 — 내용을 읽는 것은 붙여넣기(파일 조작)의
/// 일이고 그쪽은 비동기다.
/// </para>
/// </summary>
public interface IClipboardBridge
{
    void SetCopy(IReadOnlyList<LocationId> items);

    void SetCut(IReadOnlyList<LocationId> items);

    /// <summary>
    /// 붙여넣을 것이 있으면 true. <paramref name="isMove"/> 가 true 면 잘라내기였다.
    /// <para>
    /// false 를 낼 때도 <paramref name="items"/> 는 빈 목록이다 — null 을 내면 반환값을
    /// 무시한 호출부가 터진다. 다른 앱이 클립보드를 채우는 것은 우리가 관측할 수 없으므로
    /// 이 조회는 언제든 결과가 달라질 수 있다.
    /// </para>
    /// </summary>
    bool TryGetPaste(out IReadOnlyList<LocationId> items, out bool isMove);
}
