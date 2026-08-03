using FlexDir.Core.Locations;

namespace FlexDir.Core.ViewState;

/// <summary>
/// 폴더별 뷰 상태와 전역 상태의 저장소 포트 (docs/ARCHITECTURE.md §2).
/// <para>
/// 저장 위치(<c>%LOCALAPPDATA%\flex-dir\</c>)와 직렬화 포맷은 구현체의 자유다.
/// 포트가 포맷을 규정하면 나중에 바꿀 수 없다. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
/// </para>
/// <para>
/// 모든 호출은 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3) — 저장 위치가
/// 네트워크 드라이브면 초 단위로 블로킹된다. 그래서 전부 취소 가능한 비동기다.
/// </para>
/// </summary>
public interface IViewStateStore
{
    /// <summary>기억된 상태가 없으면 null. 처음 방문하는 폴더는 오류가 아니다.</summary>
    ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct);

    ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct);

    /// <summary>
    /// 저장된 것이 없으면 <see cref="GlobalViewState.Default"/>. null 을 내지 않는다 —
    /// 호출자가 매번 기본값을 조립하면 기본값이 두 군데에 생긴다.
    /// </summary>
    ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct);

    ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct);
}
