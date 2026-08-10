using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

namespace FlexDir.Core.Operations;

/// <summary>메뉴가 뜰 화면 좌표 (물리 픽셀).</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// 컨텍스트 메뉴 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에 둔다 —
/// <c>IContextMenu</c> + <c>IContextMenu2/3</c> 메시지 펌핑이고 수동 검증 대상이다
/// (ADR-009 · docs/SHELL_NOTES.md §컨텍스트 메뉴).
/// <para>
/// <b>창 핸들이 없다</b> (사용자 결정 2026-08-06). <c>FlexDir.Core</c> 는 <c>HWND</c> 를
/// 모르므로 (CLAUDE.md §1) 소유 창은 <c>FlexDir.Host</c> 가 쥐고 조립할 때 구현체에 물려
/// 준다. 대가는 "어느 창 위에" 를 호출자가 못 정한다는 것이고, 창이 하나라는 전제
/// (<c>ResidentWindow</c> · ADR-003)에 기댄다. 같은 배선이
/// <see cref="IFileOperations"/> 의 shell 대화상자에 소유 창을 준다.
/// </para>
/// <para>
/// <b>shell verb 의 결과는 돌려주지 않는다.</b> verb 는 파일시스템을 직접 고치고 우리는
/// <c>IFolderWatcher</c> 로 그것을 본다 — 진실원천은 파일시스템이다 (CLAUDE.md §4).
/// </para>
/// <para>
/// <b>앱 자체 항목은 돌려준다</b> (사용자 결정 2026-08-10). v1 은 그것을 넣지 않았고 사유는
/// *"조작이 전부 키보드 맵과 툴바에 있다"* 였는데, 즐겨찾기 추가는 폴더를 <b>가리키며</b>
/// 하는 조작이라 그 목록에 들어가지 않는다. 대가는 <c>SHELL_NOTES</c> 가 적어 둔 그대로다:
/// verb ID 범위를 우리가 갈라야 하고 (함정 3), 앱 항목은 메뉴가 완전히 풀린 뒤에 처리해야
/// 한다 (함정 8).
/// </para>
/// </summary>
public interface IContextMenuProvider
{
    /// <summary>
    /// 컨텍스트 메뉴를 띄우고, 사용자가 고른 verb 가 끝날 때까지 기다린다.
    /// <para>
    /// <paramref name="items"/> 가 비어 있으면 <paramref name="folder"/> 의 <b>배경 메뉴</b>다.
    /// shell 에서 둘은 다른 API 이므로 (<c>GetUIObjectOf</c> 대 <c>CreateViewObject</c>)
    /// 그 구분을 여기서 넘긴다.
    /// </para>
    /// <para>
    /// <paramref name="appCommands"/> 는 메뉴 <b>맨 위</b>에 붙일 앱 자체 항목의 이름이다.
    /// 사용자가 그중 하나를 고르면 그 <b>인덱스</b>가 나오고, shell verb 를 골랐거나 아무것도
    /// 고르지 않고 닫았으면 <see langword="null"/> 이다 — 앱 항목은 shell 이 아니라 부른
    /// 쪽이 처리한다.
    /// </para>
    /// <para>
    /// 사용자가 아무것도 고르지 않고 닫아도 정상 종료다. 메뉴를 세우지 못하면
    /// <see cref="LocationAccessException"/>.
    /// </para>
    /// </summary>
    Task<int?> ShowAsync(
        IReadOnlyList<LocationId> items,
        LocationId folder,
        ScreenPoint at,
        IReadOnlyList<string> appCommands,
        CancellationToken ct);
}
