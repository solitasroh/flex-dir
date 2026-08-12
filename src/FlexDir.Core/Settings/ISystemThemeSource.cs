namespace FlexDir.Core.Settings;

/// <summary>
/// OS 가 지금 라이트인지 다크인지 묻는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다
/// (docs/PRD-v2.md §19).
/// <para>
/// <b>레지스트리 값 하나를 읽을 뿐인데 왜 비동기인가</b>: 이 코드베이스의 다른 시스템
/// 조회 포트(<see cref="Storage.IDriveList"/> 등)가 전부 <c>Task.Run</c> 을 거치는 것과
/// 같은 원칙이다 (CLAUDE.md §3) — "누가 얼마나 걸릴지 모르는 것이 기준" 이고, 그 판단을
/// 호출하는 쪽마다 다시 하게 만들지 않는다.
/// </para>
/// <para>
/// <b>캐시하지 않는다.</b> OS 설정이 바뀌면(Windows 설정 앱에서 라이트/다크 전환) 매번
/// 새로 읽어야 한다 — App 이 <c>WM_SETTINGCHANGE</c> 를 받을 때마다 다시 부른다.
/// </para>
/// </summary>
public interface ISystemThemeSource
{
    /// <summary>지금 OS 가 다크 모드인가.</summary>
    ValueTask<bool> ReadAsync(CancellationToken ct);
}
