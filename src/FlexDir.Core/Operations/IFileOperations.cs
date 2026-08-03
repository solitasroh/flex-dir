using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

namespace FlexDir.Core.Operations;

/// <summary>
/// 파일 조작 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에 둔다 —
/// <c>IFileOperation</c> COM 기반이고 수동 검증 대상이다 (ADR-009).
/// <para>
/// 모든 호출은 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3). 네트워크 경로·대용량 복사는
/// 초 단위로 블로킹되므로 전부 취소 가능한 비동기다.
/// </para>
/// <para>
/// <b>진행 상황을 반환값으로 요구하지 않는다.</b> 진행률·이름 충돌 대화상자는 shell 이
/// 자기 UI 로 처리하고, 우리가 모달로 다시 만들면 작업 중에 반대편 페인을 쓸 수 없다
/// (docs/UI_GUIDE.md §금지 목록). 조작 결과로 목록을 고치지도 않는다 — 외부 변경과
/// 같은 경로(<c>IFolderWatcher</c>)로 갱신한다. 진실원천은 파일시스템이다 (CLAUDE.md §4).
/// </para>
/// <para>
/// <b>이름 충돌을 구현체가 직접 해결하지 않는다.</b> shell 표준 충돌 대화상자를 그대로
/// 띄운다 (docs/PRD.md §4). 전작은 <c>FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT</c>
/// 로 모든 대화상자를 끄고 조용히 실패했다 (docs/SHELL_NOTES.md §파일 조작).
/// </para>
/// </summary>
public interface IFileOperations
{
    /// <summary>
    /// 항목들을 대상 폴더로 복사한다. 이름이 겹치면 shell 대화상자가 사용자에게 묻는다.
    /// 열 수 없거나 쓸 수 없으면 <see cref="LocationAccessException"/>.
    /// </summary>
    Task CopyAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct);

    Task MoveAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct);

    /// <summary>
    /// 휴지통으로 보낸다. 영구 삭제가 아니다.
    /// <para>
    /// <b>구현체는 <c>FOF_ALLOWUNDO</c> 만으로 부족하다.</b> 드라이브의 휴지통 할당량을
    /// 넘으면 Windows 가 말없이 영구 삭제로 바꾼다 — <c>FOFX_RECYCLEONDELETE</c> 를 함께
    /// 줘야 휴지통이 강제된다 (docs/SHELL_NOTES.md §파일 조작).
    /// </para>
    /// <para>
    /// 영구 삭제 경로(Shift+Delete 등)는 v1 에 없다. 되돌릴 수 없는 조작을 포트에 두지 않는다.
    /// </para>
    /// </summary>
    Task DeleteAsync(IReadOnlyList<LocationId> items, CancellationToken ct);

    Task RenameAsync(LocationId item, string newName, CancellationToken ct);

    /// <summary>
    /// 만들어진 폴더의 위치를 낸다. 이름이 겹치면 구현체가 유일한 이름을 만든다 —
    /// 호출자는 요청한 이름이 아니라 <b>돌아온 위치</b>를 봐야 한다.
    /// </summary>
    Task<LocationId> CreateFolderAsync(LocationId parentFolder, string name, CancellationToken ct);
}
