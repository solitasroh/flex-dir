using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 분할 프리셋의 슬롯 배치 (docs/PRD-v2.md §18 · 사용자 결정 2026-08-12).
/// <para>
/// <b>여기가 이 기능에서 자동 채점되는 전부다.</b> 격자에 실제로 앉는 것과 스플리터를 끄는
/// 손맛은 사람이 본다 (CLAUDE.md §5) — 그래서 판정을 순수 함수로 떼어 놓았다
/// (<c>SplitterSync</c> 가 이미 같은 모양이다).
/// </para>
/// <para>
/// 격자는 열 셋(왼쪽 <c>*</c> · 스플리터 6 · 오른쪽 <c>*</c>)과 행 셋(같은 모양)이다.
/// </para>
/// </summary>
public class SplitLayoutTests
{
    // ── 1분할 — 기본화면 ────────────────────────────────────────────

    [Fact]
    public void OnePane_FillsTheWholeGrid()
    {
        Assert.Equal(new PanePlacement(0, 3, 0, 3), SplitLayout.PlacementFor(1, SplitPart.Pane0));
    }

    [Theory]
    [InlineData(SplitPart.Pane1)]
    [InlineData(SplitPart.Pane2)]
    [InlineData(SplitPart.Pane3)]
    [InlineData(SplitPart.ColumnSplitter)]
    [InlineData(SplitPart.RowSplitter)]
    public void OnePane_HidesEverythingElse(SplitPart part)
    {
        // 1분할에서 스플리터가 보이면 끌 수 있는 것처럼 보이고, 끌면 아무 일도 안 일어난다.
        Assert.Null(SplitLayout.PlacementFor(1, part));
    }

    // ── 2분할 — 좌우 ────────────────────────────────────────────────

    [Fact]
    public void TwoPanes_SplitTheColumns()
    {
        Assert.Equal(new PanePlacement(0, 3, 0, 1), SplitLayout.PlacementFor(2, SplitPart.Pane0));
        Assert.Equal(new PanePlacement(0, 3, 2, 1), SplitLayout.PlacementFor(2, SplitPart.Pane1));
    }

    [Fact]
    public void TwoPanes_ShowOnlyTheColumnSplitter()
    {
        Assert.Equal(new PanePlacement(0, 3, 1, 1), SplitLayout.PlacementFor(2, SplitPart.ColumnSplitter));
        Assert.Null(SplitLayout.PlacementFor(2, SplitPart.RowSplitter));
    }

    // ── 3분할 — 좌 1 + 우 2단 ───────────────────────────────────────

    [Fact]
    public void ThreePanes_KeepTheFirstPaneFullHeight()
    {
        Assert.Equal(new PanePlacement(0, 3, 0, 1), SplitLayout.PlacementFor(3, SplitPart.Pane0));
    }

    [Fact]
    public void ThreePanes_StackTheRightColumn()
    {
        Assert.Equal(new PanePlacement(0, 1, 2, 1), SplitLayout.PlacementFor(3, SplitPart.Pane1));
        Assert.Equal(new PanePlacement(2, 1, 2, 1), SplitLayout.PlacementFor(3, SplitPart.Pane2));
    }

    [Fact]
    public void ThreePanes_PutTheRowSplitterInTheRightColumnOnly()
    {
        // 왼쪽에는 나눌 것이 없다. 거기까지 걸치면 1번 페인 위에 끌 수 없는 띠가 생긴다.
        Assert.Equal(new PanePlacement(1, 1, 2, 1), SplitLayout.PlacementFor(3, SplitPart.RowSplitter));
    }

    [Fact]
    public void ThreePanes_HideTheFourth()
    {
        Assert.Null(SplitLayout.PlacementFor(3, SplitPart.Pane3));
    }

    // ── 4분할 — 2×2 ────────────────────────────────────────────────

    [Fact]
    public void FourPanes_FillTheGridClockwise()
    {
        // 번호가 읽기순서(1·2·3·4)가 아니라 <b>자리가 안 옮겨가는 순서</b>다
        // (사용자에게 보인 배치 2026-08-12). 3→4 로 늘릴 때 2·3번이 제자리에 남고 1번만
        // 위로 줄어든다 — 보고 있던 폴더가 화면 반대편으로 날아가지 않는다.
        Assert.Equal(new PanePlacement(0, 1, 0, 1), SplitLayout.PlacementFor(4, SplitPart.Pane0));
        Assert.Equal(new PanePlacement(0, 1, 2, 1), SplitLayout.PlacementFor(4, SplitPart.Pane1));
        Assert.Equal(new PanePlacement(2, 1, 2, 1), SplitLayout.PlacementFor(4, SplitPart.Pane2));
        Assert.Equal(new PanePlacement(2, 1, 0, 1), SplitLayout.PlacementFor(4, SplitPart.Pane3));
    }

    [Fact]
    public void FourPanes_StretchTheRowSplitterAcrossBothColumns()
    {
        // 가로줄이 일직선이어야 격자로 보인다. 비율도 하나를 나눠 쓴다
        // (WorkspaceViewModel.RowRatio).
        Assert.Equal(new PanePlacement(1, 1, 0, 3), SplitLayout.PlacementFor(4, SplitPart.RowSplitter));
    }

    // ── 자리가 옮겨가지 않는다 ──────────────────────────────────────
    // 프리셋을 고른 이유가 이것이다. 늘릴 때 이미 있던 페인이 열을 바꾸면 보고 있던
    // 폴더가 화면 반대편으로 날아간다.

    [Theory]
    [InlineData(SplitPart.Pane0)]
    [InlineData(SplitPart.Pane1)]
    [InlineData(SplitPart.Pane2)]
    [InlineData(SplitPart.Pane3)]
    public void GrowingTheSplit_NeverMovesAPaneToAnotherColumn(SplitPart part)
    {
        int? column = null;

        for (var count = 1; count <= 4; count++)
        {
            if (SplitLayout.PlacementFor(count, part) is not { } placement)
            {
                continue;
            }

            // 1분할의 0번만 두 열에 걸친다 — 그때는 아직 나눌 것이 없다.
            if (placement.ColumnSpan == 3)
            {
                continue;
            }

            column ??= placement.Column;

            Assert.Equal(column, placement.Column);
        }
    }

    [Fact]
    public void GrowingTheSplit_OnlyEverAddsPanes()
    {
        // 어떤 수에서든 보이는 슬롯은 앞에서부터 연속이어야 한다. 가운데가 비면
        // WorkspaceViewModel 의 "앞에서부터 SplitCount 개" 전제가 깨진다.
        for (var count = 1; count <= 4; count++)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var visible = SplitLayout.PlacementFor(count, (SplitPart)slot) is not null;

                Assert.Equal(slot < count, visible);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public void CountOutsideTheRange_IsReadAsTheNearestPreset(int count)
    {
        // 바인딩이 아직 서지 않은 순간에는 0 이 온다. 던지면 창이 안 뜬다 —
        // 배치는 화면의 일이고, 화면은 값 하나로 사라지면 안 된다.
        Assert.NotNull(SplitLayout.PlacementFor(count, SplitPart.Pane0));
    }
}
