using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 툴바 접기 — 폭이 모자라면 덜 중요한 무리를 접고 <c>»</c> 로 보낸다 (phase 3 step 2).
/// <para>
/// 재는 것은 순수 함수 하나다. <see cref="ToolbarOverflowPanel.Folded"/> 는 "어느 무리가
/// 접히는가". 실제 배치는 그것을 부르는 배관이라 여기서 창을 띄우지 않는다 —
/// <c>TabStripPanel.Widths</c> 와 같은 수다.
/// </para>
/// </summary>
public class ToolbarOverflowPanelTests
{
    // ── 접기 판정 ─────────────────────────────────────────────────

    [Fact]
    public void Folded_WithRoomToSpare_FoldsNothing()
    {
        Assert.Equal(
            [false, false, false],
            ToolbarOverflowPanel.Folded([0, 1, 2], [50, 30, 30], available: 200, overflowWidth: 10));

        // 딱 맞아도 접지 않는다 — 접기는 모자랄 때만이다.
        Assert.Equal(
            [false, false, false],
            ToolbarOverflowPanel.Folded([0, 1, 2], [50, 30, 30], available: 110, overflowWidth: 10));
    }

    [Fact]
    public void Folded_WhenShort_FoldsTheSmallestOrderFirst()
    {
        // 110 > 100 이라 접기 시작. 예산은 100 - 10 = 90 이고 무리 1 을 접으면 80 이라 멈춘다.
        Assert.Equal(
            [false, true, false],
            ToolbarOverflowPanel.Folded([0, 1, 2], [50, 30, 30], available: 100, overflowWidth: 10));
    }

    [Fact]
    public void Folded_WhenStillShort_FoldsTheNextOrder()
    {
        // 예산 60. 무리 1 을 접어도 80 이라 무리 2 까지 접는다 — 즉 1 → 2 → 3 순서다.
        Assert.Equal(
            [false, true, true],
            ToolbarOverflowPanel.Folded([0, 1, 2], [50, 30, 30], available: 70, overflowWidth: 10));
    }

    [Fact]
    public void Folded_OrderZero_NeverFolds()
    {
        // 접을 무리가 없으면 아무리 좁아도 그대로 넘친다 — 이동 버튼이 사라지는 것보다 낫다.
        Assert.Equal(
            [false, false],
            ToolbarOverflowPanel.Folded([0, 0], [50, 50], available: 40, overflowWidth: 10));
    }

    [Fact]
    public void Folded_TheSameOrder_FoldsAsOneGroup()
    {
        // 30 하나만 접어도 80 ≤ 95 지만 같은 번호는 한 덩어리다 — 뷰 모드 4개 중 2개만
        // 사라지면 남은 것이 무슨 뜻인지 알 수 없다.
        Assert.Equal(
            [false, true, true],
            ToolbarOverflowPanel.Folded([0, 1, 1], [50, 30, 30], available: 105, overflowWidth: 10));
    }

    [Fact]
    public void Folded_OnceFoldingStarts_ReservesTheOverflowWidth()
    {
        // » 폭 25 를 빼면 예산 75 라 무리 1(80)로는 모자라 무리 2 까지 접는다.
        // » 를 무시하면 예산 100 으로 읽어 무리 1 만 접고 — 그 » 가 밀려 넘친다.
        Assert.Equal(
            [false, true, true],
            ToolbarOverflowPanel.Folded([0, 1, 2], [50, 30, 30], available: 100, overflowWidth: 25));
    }

    [Fact]
    public void Folded_WithoutAConstraint_FoldsNothing()
    {
        // 측정이 무한으로 오는 자리(무한 폭 프로브)에서는 "모자라다" 가 성립하지 않는다.
        Assert.Equal(
            [false, false, false],
            ToolbarOverflowPanel.Folded(
                [0, 1, 2], [500, 300, 300], double.PositiveInfinity, overflowWidth: 10));
    }

    [Fact]
    public void Folded_WhenGroupsRunOut_LetsItOverflow()
    {
        // 다 접어도 100 > 45 지만 예외를 던지지 않는다 — 넘치는 쪽을 고른 것이다
        // (TabStripPanel.Widths 가 최소에서 멈추고 넘치게 두는 것과 같은 판단).
        Assert.Equal(
            [false, true],
            ToolbarOverflowPanel.Folded([0, 1], [100, 20], available: 50, overflowWidth: 5));
    }

    [Fact]
    public void Folded_WithMismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ToolbarOverflowPanel.Folded([0, 1], [50], available: 100, overflowWidth: 10));
    }

    [Fact]
    public void Folded_WithNoChildren_IsEmpty()
    {
        Assert.Empty(ToolbarOverflowPanel.Folded([], [], available: 100, overflowWidth: 10));
    }

    // ── 실제 툴바 구성 (1구간의 자식 그대로) ──────────────────────
    //
    // 뒤로(0,32) 앞으로(0,32) 상위(0,32) 구분자(0,9) 새로고침(0,32) · 구분자(3,9)
    // 자세히(3,32) 목록(3,32) 타일(3,32) 큰아이콘(3,32) · 구분자(2,9) 분류(2,32) · 📍(0,32)
    // 합 347 · » 폭 32.
    //
    // 접힘 순서는 확정 규칙(① 외부도구 → ② 분류 → ③ 뷰모드)대로 작은 번호 먼저다.
    // 그래서 "한 무리만 접힌 중간 상태" 는 분류(41)만 접어서 충분한 폭 — 306 ≤ 예산 —
    // 즉 available ∈ [338, 347) 에서 나온다. 260 에서는 분류를 접어도 306 > 228 이라
    // 뷰 모드까지 함께 접힌다.

    private static readonly int[] ToolbarOrders = [0, 0, 0, 0, 0, 3, 3, 3, 3, 3, 2, 2, 0];

    private static readonly double[] ToolbarWidths = [32, 32, 32, 9, 32, 9, 32, 32, 32, 32, 9, 32, 32];

    [Fact]
    public void Folded_TheRealToolbar_WhenWide_FoldsNothing()
    {
        Assert.All(
            ToolbarOverflowPanel.Folded(ToolbarOrders, ToolbarWidths, available: 400, overflowWidth: 32),
            folded => Assert.False(folded));
    }

    [Fact]
    public void Folded_TheRealToolbar_WhenMiddling_FoldsOnlyTheGroupingMenu()
    {
        // 347 > 340 이라 접기 시작. 분류 무리(구분자 9 + 버튼 32)를 접으면 306 ≤ 308 이라
        // 뷰 모드 4종은 남는다.
        var folded = ToolbarOverflowPanel.Folded(
            ToolbarOrders, ToolbarWidths, available: 340, overflowWidth: 32);

        Assert.Equal(
            [false, false, false, false, false, false, false, false, false, false, true, true, false],
            folded);
    }

    [Fact]
    public void Folded_TheRealToolbar_WhenNarrow_FoldsGroupingAndViewModes()
    {
        // 예산 228. 분류를 접어도 306 이라 뷰 모드까지 접어 169 ≤ 228 에서 멈춘다.
        var folded = ToolbarOverflowPanel.Folded(
            ToolbarOrders, ToolbarWidths, available: 260, overflowWidth: 32);

        Assert.Equal(
            [false, false, false, false, false, true, true, true, true, true, true, true, false],
            folded);
    }

    [Fact]
    public void Folded_TheRealToolbar_WhenTooNarrow_KeepsTheNavigationAndThePin()
    {
        // 다 접어도 169 > 168 이지만 이동 4개 + 구분자 + 📍 (FoldOrder 0)는 어떤 경우에도
        // 남는다 — 모자란 1px 은 넘친다.
        var folded = ToolbarOverflowPanel.Folded(
            ToolbarOrders, ToolbarWidths, available: 200, overflowWidth: 32);

        Assert.Equal(
            [false, false, false, false, false, true, true, true, true, true, true, true, false],
            folded);
    }
}
