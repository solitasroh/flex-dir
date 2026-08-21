namespace FlexDir.Core.Locations;

/// <summary>
/// 알려진 폴더 다섯 고정. <b>순서가 곧 메뉴에 뜨는 순서다</b> — 뒤바꾸면 화면이 바뀐다.
/// 설정은 없다 (docs/PRD-v2.md 알려진 폴더 메뉴).
/// </summary>
public enum KnownFolderKind
{
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
}

/// <summary>
/// 알려진 폴더 하나. <paramref name="Location"/> 이 <see langword="null"/> 이면
/// 이 기계에 그 폴더가 없다는 뜻이다.
/// <para>
/// <b><paramref name="Label"/> 은 구현체가 채운다.</b> 표시 문자열이 Core 에 박히면
/// <see cref="KnownFolderKind"/> 하나를 늘릴 때 두 곳을 고쳐야 하고, OS 가 실제로 쓰는
/// 이름(사용자가 바탕화면을 이름변경했을 수도 있다)과 갈릴 수 있다. 라벨은 경로와
/// <b>같은 곳에서 같이 온다.</b>
/// </para>
/// </summary>
public readonly record struct KnownFolder(KnownFolderKind Kind, string Label, LocationId? Location);

/// <summary>
/// 알려진 폴더를 세는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
/// <para>
/// <b>왜 포트인가</b>: 알려진 폴더 조회는 저장소·레지스트리에 닿는다. 리디렉션된 폴더
/// (OneDrive · 도메인 로밍 프로필)에서는 네트워크로 내려가 초 단위로 블로킹하므로
/// UI 스레드에서 부를 수 없고 (CLAUDE.md §3), 경로를 구하는
/// <c>Environment.GetFolderPath</c>·<c>SHGetKnownFolderPath</c> 는 둘 다 Windows API 라
/// Core 가 직접 부를 수 없다 (CLAUDE.md §1).
/// </para>
/// <para>
/// <b>목록은 한 번에 온다.</b> 폴더 열거(<see cref="Enumeration.IFolderSource"/>)와 달리
/// 스트림이 아니다 — 다섯 개고, 점진적으로 그려서 얻을 것이 없다.
/// </para>
/// </summary>
public interface IKnownFolderList
{
    /// <summary>
    /// 알려진 폴더 다섯. <b>없는 폴더는 목록에서 빠지지 않고
    /// <see cref="KnownFolder.Location"/> 이 <see langword="null"/> 로 온다</b> —
    /// 구현체가 걸러 내면 호출자가 "다섯 개 중 몇 번째" 를 셀 수 없고, 무엇이 왜 없는지
    /// 진단할 길이 사라진다. 거르는 것은 ViewModel 의 일이다.
    /// <para>
    /// <b>실패는 던지지 않는다</b> — 알려진 폴더는 곁다리이고, 못 읽는 것이 폴더를 못 여는
    /// 사건이 되면 안 된다. 취소만 예외로 나온다.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken ct);
}
