using System.Security;

using FlexDir.Core.Settings;

using Microsoft.Win32;

namespace FlexDir.Shell.Settings;

/// <summary>
/// <c>AppsUseLightTheme</c> 레지스트리 값으로 <see cref="ISystemThemeSource"/> 를 구현한다.
///
/// <para>
/// <b>COM 이 아니다</b> — <see cref="FlexDir.Shell.Storage.SystemDriveList"/> ·
/// <see cref="FlexDir.Shell.Storage.FileSystemDriveSpace"/> 와 같은 자리다. STA 도 정리도
/// 필요 없다. 그래도 <c>Task.Run</c> 을 거치는 이유는 <see cref="ISystemThemeSource"/> 문서 참조.
/// </para>
///
/// <para>
/// 값이 없거나(레지스트리 키를 지원하지 않는 옛 Windows) 읽기가 실패하면 <b>라이트</b>로
/// 접는다 — Windows 자체의 기본값과 같다. 0 이 다크, 그 외(1 또는 없음)가 라이트다.
/// </para>
/// </summary>
public sealed class RegistrySystemThemeSource : ISystemThemeSource
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string ValueName = "AppsUseLightTheme";

    public async ValueTask<bool> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(Read, ct).ConfigureAwait(false);
    }

    private static bool Read()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, ValueName, 1) is int light && light == 0;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            // 못 읽으면 OS 기본값(라이트)으로 접는다 — 창이 못 뜨는 사건이 아니다.
            return false;
        }
    }
}
