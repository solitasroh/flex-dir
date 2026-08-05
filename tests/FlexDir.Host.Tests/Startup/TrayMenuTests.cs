using System.Windows.Forms;

using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// 트레이 아이콘의 메뉴 (phase B-2 · 사용자 결정 2026-08-06). 상주 프로세스(ADR-003)는
/// 창을 닫아도 남으므로, 창 없이 닿을 수 있는 완전 종료 조작이 알림 영역에 있어야 한다 —
/// 이것이 없으면 종료 수단이 Stop-Process 뿐이다.
/// <para>
/// 메뉴 구성과 클릭 → 동작 연결만 여기서 채점한다. NotifyIcon 표시와 실제 클릭은
/// 메시지 펌프가 필요해 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TrayMenuTests
{
    [Fact]
    public void Create_HasOpenAndExit_InThatOrder()
    {
        // "창 열기" 가 먼저다 — 트레이 메뉴의 첫 항목은 아이콘의 기본 동작과 같아야 한다.
        using var menu = TrayMenu.Create(() => { }, () => { });

        Assert.Collection(
            menu.Items.Cast<ToolStripItem>(),
            item => Assert.Equal("창 열기", item.Text),
            item => Assert.IsType<ToolStripSeparator>(item),
            item => Assert.Equal("완전 종료", item.Text));
    }

    [Fact]
    public void ClickingOpen_OpensTheWindow_AndDoesNotExit()
    {
        var opened = 0;
        var exited = 0;
        using var menu = TrayMenu.Create(() => opened++, () => exited++);

        ((ToolStripMenuItem)menu.Items[0]).PerformClick();

        Assert.Equal(1, opened);
        Assert.Equal(0, exited);
    }

    [Fact]
    public void ClickingExit_ExitsForReal()
    {
        var opened = 0;
        var exited = 0;
        using var menu = TrayMenu.Create(() => opened++, () => exited++);

        ((ToolStripMenuItem)menu.Items[^1]).PerformClick();

        Assert.Equal(0, opened);
        Assert.Equal(1, exited);
    }

    [Fact]
    public void Arguments_AreInvalid_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => TrayMenu.Create(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => TrayMenu.Create(() => { }, null!));
    }
}
