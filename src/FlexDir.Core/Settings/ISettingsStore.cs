using FlexDir.Core.Locations;

namespace FlexDir.Core.Settings;

/// <summary>
/// 창을 열 때 어느 폴더로 갈 것인가 (docs/PRD-v2.md §12).
/// </summary>
public enum StartFolderMode
{
    /// <summary>지난번에 보던 폴더. 기본값이다.</summary>
    LastFolder,

    /// <summary>정해 둔 폴더 하나. 늘 같은 곳에서 시작한다.</summary>
    Fixed,
}

/// <summary>
/// 색을 라이트·다크 중 무엇으로 그릴 것인가 (docs/PRD-v2.md §19).
/// </summary>
public enum ThemeMode
{
    /// <summary>OS 설정을 따라간다. 기본값이다 — 다크로 쓰던 사람 앞에 라이트 창이 뜨면 안 된다.</summary>
    System,

    /// <summary>OS 와 무관하게 늘 라이트.</summary>
    Light,

    /// <summary>OS 와 무관하게 늘 다크.</summary>
    Dark,
}

/// <summary>
/// 사용자가 정해 둔 것 (docs/PRD-v2.md §12). <b>뷰 상태와 다른 파일에 산다</b> —
/// 아래 <see cref="ISettingsStore"/> 참조.
/// </summary>
public sealed record AppSettings
{
    /// <summary>마지막 폴더에서 시작하고 숨김·시스템 파일은 감춘다. 탐색기의 기본값과 같다.</summary>
    public static AppSettings Default { get; } = new();

    public StartFolderMode StartMode { get; init; } = StartFolderMode.LastFolder;

    /// <summary>
    /// <see cref="StartFolderMode.Fixed"/> 일 때 열 폴더. <b>모드와 따로 산다</b> —
    /// 모드만 고르고 폴더를 아직 정하지 않은 중간 상태가 실제로 있고, 그때 되돌아갈
    /// 자리가 있어야 한다 (<see cref="ResolveStartFolder"/>).
    /// </summary>
    public LocationId? StartFolder { get; init; }

    /// <summary>
    /// 숨김·시스템 파일을 목록과 트리에 보이는가 (<see cref="Enumeration.ItemVisibility"/>).
    /// <para>
    /// <b>토글 하나다</b> (사용자 결정 2026-08-10). 탐색기는 '숨긴 항목' 과 '보호된 운영 체제
    /// 파일' 을 따로 두지만, 그러면 <c>$Recycle.Bin</c>·<c>System Volume Information</c> 처럼
    /// 둘 다 붙은 것이 어느 한쪽만 켜서는 보이지 않아 조합이 넷이 된다.
    /// </para>
    /// </summary>
    public bool ShowHiddenItems { get; init; }

    /// <summary>
    /// 라이트·다크·시스템 중 무엇을 쓰는가. 기본은 <see cref="ThemeMode.System"/> —
    /// 처음 켠 사람은 OS 를 따라간다 (<see cref="ResolveIsDarkMode"/>).
    /// </summary>
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>
    /// 지금 화면을 다크로 그려야 하는가. <paramref name="systemIsDark"/> 는 OS 설정이고
    /// <see cref="ThemeMode.System"/> 일 때만 쓰인다.
    /// <para>
    /// Host 와 <c>SettingsViewModel</c> 양쪽이 같은 답을 내야 해서 여기 있다 —
    /// <see cref="ResolveStartFolder"/> 와 같은 이유다.
    /// </para>
    /// </summary>
    public bool ResolveIsDarkMode(bool systemIsDark) => Theme switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => systemIsDark,
    };

    /// <summary>
    /// 페인 하나가 시작할 폴더. <paramref name="lastFolder"/> 는 그 페인이 마지막으로 보던
    /// 곳이고 <paramref name="fallback"/> 은 기억이 없을 때의 자리다.
    ///
    /// <para>
    /// <b>존재를 확인하지 않는다.</b> Core 는 파일시스템에 닿지 않고 (docs/ARCHITECTURE.md §1),
    /// 사라진 폴더는 페인이 상위로 올라가며 처리한다 (docs/PRD.md §4). 여기서 확인하면 시작
    /// 경로에 저장소 호출이 하나 더 생기는데 그것이 네트워크 경로면 초 단위다 (CLAUDE.md §3).
    /// </para>
    /// </summary>
    public LocationId? ResolveStartFolder(LocationId? lastFolder, LocationId? fallback)
        => StartMode == StartFolderMode.Fixed && StartFolder is { } chosen
            ? chosen
            : lastFolder ?? fallback;
}

/// <summary>
/// 설정 저장소 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
///
/// <para>
/// <b><see cref="ViewState.IViewStateStore"/> 와 나누는 이유</b>: 설정은 캐시가 아니라
/// <b>사용자의 의도</b>다. 뷰 상태는 어긋나면 버리고 파일시스템을 믿으면 되지만
/// (CLAUDE.md §4), 설정을 같은 파일에 두면 그 파일이 깨져서 기본값으로 접히는 사건이
/// "숨김 파일을 보겠다"·"여기서 시작하겠다" 를 조용히 뒤집는다.
/// <see cref="Favorites.IFavoriteStore"/> 와 같은 판단이고 파일도 따로 쓴다.
/// </para>
///
/// <para>
/// <b>전체를 주고받는다.</b> 항목마다 API 를 두면 새 설정을 넣을 때마다 포트가 넓어진다.
/// </para>
///
/// <para>
/// 모든 호출은 UI 스레드 밖에서 끝나야 한다 (CLAUDE.md §3) — 상태 폴더가 네트워크 경로일
/// 수 있다.
/// </para>
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// 저장된 설정. 없으면 <see cref="AppSettings.Default"/> 다 — 처음 켠 사람에게는 없는
    /// 것이 정상이다.
    /// </summary>
    ValueTask<AppSettings> LoadAsync(CancellationToken ct);

    /// <summary>설정 전체를 남긴다.</summary>
    ValueTask SaveAsync(AppSettings settings, CancellationToken ct);
}
