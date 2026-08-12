using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// OS 가 라이트/다크를 바꿀 때 오는 <c>WM_SETTINGCHANGE</c> 를 창에 걸어 설정에 알린다
/// (docs/PRD-v2.md §19).
/// <para>
/// <b>포트가 이벤트를 내지 않는 이유</b>: <see cref="FlexDir.Core.Settings.ISystemThemeSource"/>
/// 는 값을 읽기만 한다 — "언제 다시 읽을지" 는 창이 안다. Windows 설정 앱에서 테마를 바꾸면
/// 이 메시지가 모든 최상위 창에 브로드캐스트된다 (탐색기가 같은 방식으로 자신을 다시 그린다).
/// </para>
/// <para>
/// 실제로 창에 훅을 거는 것은 <see cref="HwndSource"/> 가 필요해 사람이 확인한다
/// (CLAUDE.md §5 · <c>MaximizedFrame</c>·<c>ResidentWindow</c> 와 같은 나눔). 여기서
/// 채점하는 것은 <see cref="IsImmersiveColorChange"/> 하나뿐이다.
/// </para>
/// </summary>
public static class SystemThemeWatcher
{
    private const int WmSettingChange = 0x001A;
    private const string ImmersiveColorSet = "ImmersiveColorSet";

    /// <summary>
    /// 이 메시지가 라이트/다크 전환 브로드캐스트인가.
    ///
    /// <para>
    /// 같은 <c>WM_SETTINGCHANGE</c> 로 글꼴·배경화면 등 다른 설정도 온다 — 매번
    /// <see cref="SettingsViewModel.RefreshSystemTheme"/> 를 부르면 레지스트리를 쓸데없이
    /// 다시 읽는다.
    /// </para>
    ///
    /// <para>
    /// <b>이름을 문자열이 아니라 지연 함수로 받는다.</b> 2026-08-12 에 실물에서
    /// <see cref="AccessViolationException"/> 으로 프로세스가 죽은 자리다 — 훅은 <b>모든</b>
    /// 창 메시지를 받고, <c>WM_SETTINGCHANGE</c> 가 아닌 메시지의 <c>lParam</c> 은 문자열
    /// 포인터가 아니다(좌표·핸들·구조체 포인터). 판정 전에 마샬링하면 그것을 문자열로 읽다가
    /// 보호된 메모리를 훑는다. 함수로 받으면 <b>메시지가 맞을 때만</b> 읽고, "읽지 않았다" 가
    /// 테스트에 드러난다.
    /// </para>
    /// </summary>
    internal static bool IsImmersiveColorChange(int msg, Func<string?> settingName)
    {
        ArgumentNullException.ThrowIfNull(settingName);

        // 순서가 이 함수의 존재 이유다 — msg 를 먼저 가른 뒤에만 이름을 읽는다.
        return msg == WmSettingChange && settingName() == ImmersiveColorSet;
    }

    /// <summary>
    /// 창에 훅을 건다. <b>켜는 것은 한 번뿐이다</b> — 끄는 경로가 없으므로 해제 코드도 없다
    /// (WindowCaption·MaximizedFrame 과 같은 이유).
    /// </summary>
    public static void Attach(Window window, SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);

        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
            {
                source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr lParam, ref bool handled) =>
                {
                    // lParam 은 메시지가 맞을 때만 읽힌다 (IsImmersiveColorChange 주석).
                    // WM_SETTINGCHANGE 라도 0 으로 오는 경우가 있어 그것도 여기서 막는다.
                    var name = () => lParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(lParam);

                    if (IsImmersiveColorChange(msg, name))
                    {
                        // 기다리지 않는다 — 창 프로시저를 붙잡으면 안 된다. 실패는
                        // RefreshSystemTheme 자신이 삼킨다.
                        _ = settings.RefreshSystemTheme();
                    }

                    return IntPtr.Zero;
                });
            }
        };
    }
}
