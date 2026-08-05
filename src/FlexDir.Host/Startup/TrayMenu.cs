using System.Windows.Forms;

namespace FlexDir.Host.Startup;

/// <summary>
/// 트레이 아이콘의 메뉴 (phase B-2 · 사용자 결정 2026-08-06: 완전 종료는 트레이).
/// <para>
/// 상주 프로세스(ADR-003)는 창을 닫아도 남는다. 그래서 완전 종료 조작은 <b>창 없이 닿을
/// 수 있는 곳</b>에 있어야 하고, 그 자리가 알림 영역이다 — 상주 중임이 눈에 보이는 것도
/// 이 아이콘이다.
/// </para>
/// <para>
/// 여기는 메뉴 구성과 클릭 → 동작 연결까지다. <c>NotifyIcon</c> 자체는 Program 이 든다 —
/// 아이콘을 보이고 접는 것은 순서의 일이지 판단이 아니다.
/// </para>
/// </summary>
public static class TrayMenu
{
    /// <param name="openWindow">창을 요구한다 — 활성화 경로와 같다 (사용 기록 포함).</param>
    /// <param name="exit">완전 종료. 유일한 <c>Application.Shutdown</c> 경로다.</param>
    public static ContextMenuStrip Create(Action openWindow, Action exit)
    {
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(exit);

        var menu = new ContextMenuStrip();

        // "창 열기" 가 먼저다 — 트레이 메뉴의 첫 항목은 아이콘의 기본 동작(더블클릭)과
        // 같아야 한다. 구분선은 파괴적인 항목(종료)을 실수 클릭에서 떼어 놓는다.
        menu.Items.Add("창 열기", null, (_, _) => openWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("완전 종료", null, (_, _) => exit());

        return menu;
    }
}
