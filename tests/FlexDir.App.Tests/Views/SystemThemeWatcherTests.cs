using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// OS 의 라이트/다크 전환을 알리는 <c>WM_SETTINGCHANGE</c> 를 거르는 판정
/// (docs/PRD-v2.md §19).
/// <para>
/// 창에 실제로 훅을 거는 것은 <see cref="System.Windows.Interop.HwndSource"/> 가 필요해
/// 사람이 확인한다 (CLAUDE.md §5) — <c>MaximizedFrame</c>·<c>ResidentWindow</c> 와 같은
/// 나눔이다.
/// </para>
/// <para>
/// <b>여기서 채점하는 것의 본체는 "언제 <c>lParam</c> 을 읽는가" 다.</b> 2026-08-12 에
/// 실물에서 <see cref="AccessViolationException"/> 으로 프로세스가 죽었다 — 판정 전에
/// 무조건 <c>Marshal.PtrToStringUni(lParam)</c> 를 불렀고, <c>WM_SETTINGCHANGE</c> 가 아닌
/// 메시지의 <c>lParam</c> 은 문자열 포인터가 아니다. 그래서 이름을 <b>지연 함수</b>로
/// 받는다 — 부르지 않았다는 것이 채점된다.
/// </para>
/// </summary>
public class SystemThemeWatcherTests
{
    private const int WmSettingChange = 0x001A;

    [Fact]
    public void IsImmersiveColorChange_TheExactMessageAndParameter_IsTrue()
    {
        // Windows 설정 앱에서 라이트/다크를 바꾸면 이 조합으로 브로드캐스트한다.
        Assert.True(SystemThemeWatcher.IsImmersiveColorChange(WmSettingChange, () => "ImmersiveColorSet"));
    }

    [Fact]
    public void IsImmersiveColorChange_ADifferentSetting_IsFalse()
    {
        // 같은 메시지로 글꼴·배경화면 등 다른 설정도 온다 — 매번 다시 그리면 안 된다.
        Assert.False(SystemThemeWatcher.IsImmersiveColorChange(WmSettingChange, () => "WindowMetrics"));
    }

    [Fact]
    public void IsImmersiveColorChange_ADifferentMessage_DoesNotEvenLookAtTheParameter()
    {
        // **이것이 실물에서 프로세스를 죽인 자리다** (2026-08-12). WM_MOUSEMOVE 의 lParam 은
        // 좌표이고, WM_NCHITTEST 의 것도 문자열이 아니다 — 읽는 순간 보호된 메모리를 훑는다.
        var read = 0;

        var change = SystemThemeWatcher.IsImmersiveColorChange(0x0200, () =>
        {
            read++;
            return "ImmersiveColorSet";
        });

        Assert.False(change);
        Assert.Equal(0, read);
    }

    [Fact]
    public void IsImmersiveColorChange_NoParameterName_IsFalse()
    {
        // WM_SETTINGCHANGE 라도 lParam 이 0 으로 오는 경우가 있다 (SPI_* 계열). 그때
        // 마샬링하면 같은 사고가 난다 — 훅이 null 을 내고 여기서 걸러진다.
        Assert.False(SystemThemeWatcher.IsImmersiveColorChange(WmSettingChange, () => null));
    }
}
