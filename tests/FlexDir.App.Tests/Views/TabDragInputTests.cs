using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 탭 드래그 — 같은 페인 안의 순서 바꾸기와 반대편 페인으로 보내기 (docs/PRD-v2.md §17 ·
/// docs/DESIGN.md §1-1 §탭 드래그 중).
/// <para>
/// 소유권 이동은 이미 서서 채점됐다 (<c>PaneTabsViewModel.SendAsync</c>) — 여기 남은 것은
/// <b>입력과 표시</b>다. 모달 루프(<c>DoDragDrop</c>)는 자동 테스트가 밟을 수 없으므로
/// (CLAUDE.md §5) 판정 셋만 잰다: 어느 틈에 놓았나 · 그 틈이 목록의 몇 번인가 · 선을 어디에
/// 그리나.
/// </para>
/// </summary>
public class TabDragInputTests
{
    /// <summary>탭 셋이 90 폭에 간격 2 로 선 줄. 실측 스펙 그대로다 (docs/DESIGN.md §1-1).</summary>
    private static readonly Rect[] Strip =
    [
        new(0, 0, 90, 24),
        new(92, 0, 90, 24),
        new(184, 0, 90, 24),
    ];

    // ── 어느 틈에 놓았나 ──────────────────────────────────────────
    // 경계는 <b>탭의 가운데</b>다. 탭 경계로 잡으면 탭 위 어디에 놓아도 늘 그 왼쪽 틈이라
    // 오른쪽으로 한 칸 미는 조작이 불가능해진다.

    [Fact]
    public void GapAt_BeforeTheFirstTab_IsTheFirstGap()
    {
        Assert.Equal(0, TabDragInput.GapAt(Strip, 10));
    }

    [Fact]
    public void GapAt_OnTheLeftHalfOfATab_StaysBeforeIt()
    {
        Assert.Equal(1, TabDragInput.GapAt(Strip, 100));
    }

    [Fact]
    public void GapAt_OnTheRightHalfOfATab_GoesAfterIt()
    {
        Assert.Equal(2, TabDragInput.GapAt(Strip, 160));
    }

    [Fact]
    public void GapAt_PastTheLastTab_IsTheGapAfterEverything()
    {
        Assert.Equal(3, TabDragInput.GapAt(Strip, 400));
    }

    [Fact]
    public void GapAt_WithNoTabs_IsTheFirstGap()
    {
        Assert.Equal(0, TabDragInput.GapAt([], 400));
    }

    // ── 그 틈이 목록의 몇 번인가 ──────────────────────────────────

    [Fact]
    public void Target_MovingForward_StepsBackOne()
    {
        // 끌던 탭을 목록에서 빼는 순간 그 뒤가 한 칸 당겨진다. 보정하지 않으면 놓은 자리보다
        // 한 칸 오른쪽에 선다 — 화면과 결과가 갈리는 자리다.
        Assert.Equal(1, TabDragInput.Target(from: 0, gap: 2));
        Assert.Equal(2, TabDragInput.Target(from: 0, gap: 3));
    }

    [Fact]
    public void Target_MovingBackward_IsTheGapItself()
    {
        // 뒤로 끌 때는 빠지는 자리가 목표보다 뒤라 당겨지는 것이 없다.
        Assert.Equal(0, TabDragInput.Target(from: 2, gap: 0));
        Assert.Equal(1, TabDragInput.Target(from: 2, gap: 1));
    }

    [Fact]
    public void Target_OnItsOwnEdges_StaysPut()
    {
        // 자기 왼쪽 틈과 오른쪽 틈은 둘 다 "안 움직인다" 여야 한다. 한쪽만 맞으면 제자리에
        // 놓은 것이 한 칸 밀린다.
        Assert.Equal(1, TabDragInput.Target(from: 1, gap: 1));
        Assert.Equal(1, TabDragInput.Target(from: 1, gap: 2));
    }

    // ── 선을 어디에 그리나 (2px --accent 세로선) ───────────────────

    [Fact]
    public void LineAt_TheFirstGap_IsTheLeftEdge()
    {
        Assert.Equal(0, TabDragInput.LineAt(Strip, 0));
    }

    [Fact]
    public void LineAt_BetweenTwoTabs_IsTheMiddleOfTheGap()
    {
        // 탭 사이 간격이 2 이므로 90 과 92 의 가운데다.
        Assert.Equal(91, TabDragInput.LineAt(Strip, 1));
    }

    [Fact]
    public void LineAt_TheLastGap_IsTheRightEdgeOfTheLastTab()
    {
        Assert.Equal(274, TabDragInput.LineAt(Strip, 3));
    }

    [Fact]
    public void LineAt_WithNoTabs_IsTheLeftEdge()
    {
        Assert.Equal(0, TabDragInput.LineAt([], 0));
    }
}
