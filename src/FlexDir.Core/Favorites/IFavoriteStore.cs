using FlexDir.Core.Locations;

namespace FlexDir.Core.Favorites;

/// <summary>
/// 사용자가 트리에 고정해 둔 폴더 하나 (docs/PRD-v2.md §10-2).
/// <para>
/// <paramref name="Label"/> 은 바꿀 수 있다 — 같은 이름의 폴더가 여럿일 때
/// (<c>build</c> 가 셋일 수 있다) 그것을 가르는 유일한 수단이다.
/// </para>
/// </summary>
public readonly record struct Favorite(LocationId Path, string Label);

/// <summary>
/// 즐겨찾기 저장소 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
///
/// <para>
/// <b><see cref="ViewState.IViewStateStore"/> 와 나누는 이유</b>: 즐겨찾기는 캐시가 아니라
/// <b>사용자가 만든 데이터</b>다. 뷰 상태는 어긋나면 버리고 파일시스템을 믿으면 되지만
/// (CLAUDE.md §4), 즐겨찾기는 파일시스템 어디에도 없다 — 잃으면 복구할 곳이 없다.
/// 그래서 파일도 따로 쓴다: 뷰 상태 파일이 깨져서 기본값으로 접히는 사건이 즐겨찾기를
/// 함께 지우면 안 된다.
/// </para>
///
/// <para>
/// <b>목록 전체를 주고받는다.</b> 추가·제거·이름·순서를 각각 API 로 두면 "중복을 어떻게
/// 다루나" 같은 규칙이 저장소와 ViewModel 두 곳에 생긴다. 규칙은 ViewModel 하나가 갖고
/// 저장소는 받은 목록을 그대로 남긴다.
/// </para>
///
/// <para>
/// 모든 호출은 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3) — 상태 폴더가 네트워크 경로일
/// 수 있다.
/// </para>
/// </summary>
public interface IFavoriteStore
{
    /// <summary>
    /// 저장된 즐겨찾기. 없으면 빈 목록이다 — 처음 켠 사람에게는 없는 것이 정상이다.
    /// <b>순서가 곧 사용자가 정한 순서다.</b>
    /// </summary>
    ValueTask<IReadOnlyList<Favorite>> LoadAsync(CancellationToken ct);

    /// <summary>
    /// 목록 전체를 남긴다. 호출자가 나중에 자기 목록을 바꿔도 저장된 것이 흔들리면 안 된다.
    /// </summary>
    ValueTask SaveAsync(IReadOnlyList<Favorite> favorites, CancellationToken ct);
}
