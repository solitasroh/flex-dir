using FlexDir.Core.Locations;

namespace FlexDir.Core.Storage;

/// <summary>
/// 트리 루트에 서는 드라이브 하나.
/// <para>
/// <paramref name="Server"/> 는 매핑된 네트워크 드라이브일 때만 채워진다
/// (<c>Z:</c> → <c>\\10.10.10.23</c>). 트리는 이것을 모아 네트워크 서버 항목을 만든다 —
/// <b>서버 목록을 따로 찾아다니지 않는 것이 결정이다</b> (사용자 결정 2026-08-10):
/// 네트워크 이웃 열거는 없는 서버 하나에 42초를 쓴다 (docs/PRD-v2.md §5 N-4 실측).
/// </para>
/// </summary>
public readonly record struct DriveEntry(LocationId Root, string Label, LocationId? Server);

/// <summary>
/// 이 기계에 붙어 있는 드라이브를 세는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
/// <para>
/// <b>왜 포트인가</b>: 드라이브 열거는 저장소에 닿는다. <c>DriveInfo.GetDrives</c> 는
/// 연결이 끊긴 매핑 드라이브에서 초 단위로 블로킹하므로 UI 스레드에서 부를 수 없고
/// (CLAUDE.md §3), 구현이 Core 안에 있으면 트리 테스트가 이 기계의 드라이브 구성에
/// 따라 갈린다.
/// </para>
/// <para>
/// <b>목록은 한 번에 온다.</b> 폴더 열거(<see cref="Enumeration.IFolderSource"/>)와 달리
/// 스트림이 아니다 — 드라이브는 많아야 수십 개고, 점진적으로 그려서 얻을 것이 없다.
/// </para>
/// </summary>
public interface IDriveList
{
    /// <summary>
    /// 지금 붙어 있는 드라이브. 하나도 없으면 빈 목록이다 (<c>null</c> 이 아니다).
    /// <b>실패는 던지지 않는다</b> — 트리는 곁다리이고, 드라이브를 못 세는 것이 폴더를
    /// 못 여는 사건이 되면 안 된다. 취소만 예외로 나온다.
    /// </summary>
    ValueTask<IReadOnlyList<DriveEntry>> ListAsync(CancellationToken ct);
}
