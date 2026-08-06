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
/// <b>무엇이 선택됐는지 돌려주지 않는다.</b> shell verb 는 파일시스템을 직접 고치고
/// 우리는 <c>IFolderWatcher</c> 로 그것을 본다 — 진실원천은 파일시스템이다 (CLAUDE.md §4).
/// 앱 자체 메뉴 항목도 넣지 않는다: v1 의 조작은 전부 키보드 맵과 툴바에 있고
/// (docs/DESIGN.md §9), 넣는 순간 verb ID 충돌을 우리가 관리해야 한다
/// (docs/SHELL_NOTES.md §컨텍스트 메뉴 함정 3).
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
    /// 사용자가 아무것도 고르지 않고 닫아도 정상 종료다. 메뉴를 세우지 못하면
    /// <see cref="LocationAccessException"/>.
    /// </para>
    /// </summary>
    Task ShowAsync(IReadOnlyList<LocationId> items, LocationId folder, ScreenPoint at, CancellationToken ct);
}
