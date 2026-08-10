using FlexDir.Core.Locations;

namespace FlexDir.Core.Storage;

/// <summary>
/// 사용자가 '네트워크 위치 추가' 로 등록해 둔 곳 하나 (docs/PRD-v2.md §10).
/// <para>
/// <paramref name="Label"/> 은 사용자가 붙인 이름이고 <paramref name="Path"/> 에서 유도하지
/// 않는다 — <c>\\서버\공유\a\b\c</c> 를 <c>dev-server</c> 로 부르려고 등록하는 것이 그
/// 기능의 요점이다. 공유 루트가 아니라 <b>깊은 폴더</b>일 수 있다는 점이
/// <see cref="DriveEntry.Server"/> 와 다르다.
/// </para>
/// </summary>
public readonly record struct NetworkPlace(LocationId Path, string Label);

/// <summary>
/// '네트워크 위치 추가' 로 등록된 곳을 세는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
///
/// <para>
/// <b><see cref="IDriveList"/> 와 나누는 이유</b>: 이쪽은 드라이브 문자를 만들지 않아
/// <c>WNetGetConnection</c> 에 잡히지 않는다. Windows 는 이것을 바로가기 파일로 저장하므로
/// 읽는 기제가 아예 다르다 (파일 열거 + COM). 공유 열거와 폴더 열거를 한 클래스에 섞지
/// 않은 것과 같은 판단이다 (docs/PRD-v2.md §5 N-3).
/// </para>
///
/// <para>
/// <b>등록된 곳이 살아 있는지 확인하지 않는다.</b> 확인하려면 네트워크로 나가야 하고,
/// 죽은 서버 하나가 트리 전체를 수십 초 붙잡는다 (CLAUDE.md §3). 닿을 수 있는지는 실제로
/// 펼치거나 열 때 알면 된다.
/// </para>
/// </summary>
public interface INetworkPlaceList
{
    /// <summary>
    /// 등록된 네트워크 위치. 하나도 없으면 빈 목록이다.
    /// <b>실패는 던지지 않는다</b> — <see cref="IDriveList"/> 와 같은 이유다.
    /// </summary>
    ValueTask<IReadOnlyList<NetworkPlace>> ListAsync(CancellationToken ct);
}
