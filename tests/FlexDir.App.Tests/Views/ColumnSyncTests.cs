using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// Details 헤더의 컬럼 폭과 <c>WorkspaceViewModel</c> 을 잇는 <see cref="ColumnSync"/>.
/// <para>
/// <see cref="SplitterSync"/>·<see cref="TreeSync"/> 와 같은 구도다 — 판정만 채점하고
/// 드래그는 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class ColumnSyncTests
{
    [Fact]
    public void WidthsFor_GivesPixelsForAllFour()
    {
        // 넷 다 픽셀이다 — 탐색기와 같이 경계를 끌면 그 열만 변하고 나머지는 폭을 지킨 채
        // 밀린다. 하나라도 star 면 그 열이 남은 공간을 먹으며 시소가 된다
        // (사용자 지적 2026-08-10: "반대로 움직인다").
        var (name, size, type, modified) = ColumnSync.WidthsFor(320, 90, 120, 140);

        Assert.Equal(GridUnitType.Pixel, name.GridUnitType);
        Assert.Equal(320, name.Value);
        Assert.Equal(90, size.Value);
        Assert.Equal(120, type.Value);
        Assert.Equal(140, modified.Value);
    }

    [Fact]
    public void WidthsFor_WithUnusableValues_FallsBackToDefaults()
    {
        // 레이아웃 전의 0 과 NaN 이 여기로 온다. 그대로 펴면 컬럼이 사라진 채로 뜬다.
        var (name, size, type, modified) = ColumnSync.WidthsFor(0, double.NaN, -5, 0);

        Assert.True(name.Value > 0);
        Assert.True(size.Value > 0);
        Assert.True(type.Value > 0);
        Assert.True(modified.Value > 0);
    }

    [Fact]
    public void Moved_ToTheRight_Widens()
    {
        // 손잡이는 그 컬럼의 오른쪽 끝이다 — 오른쪽으로 끌면 넓어지는 것이 손이 기대하는
        // 방향이고, 그 반대가 이번 지적의 내용이었다.
        Assert.Equal(140, ColumnSync.Moved(120, 20));
    }

    [Fact]
    public void Moved_ToTheLeft_Narrows()
    {
        Assert.Equal(100, ColumnSync.Moved(120, -20));
    }

    [Fact]
    public void Moved_PastTheGrip_StopsWhereItCanStillBeGrabbed()
    {
        // 0 까지 가면 손잡이가 사라져 되돌릴 수 없다.
        Assert.True(ColumnSync.Moved(60, -500) >= 32);
        Assert.True(ColumnSync.Moved(60, double.NaN) >= 32);
    }

    // ── 더블클릭으로 내용에 맞추기 (2026-09-22 사용자 요청) ──────
    //
    // 탐색기는 컬럼 경계를 더블클릭하면 그 컬럼을 <b>화면에 보이는 항목</b> 중 가장 긴
    // 것에 맞춘다. 잴 것을 모으는 것은 View 의 일이고 (가상화된 행만 실현된다),
    // 여기서 채점하는 것은 모인 값으로 폭을 정하는 규칙이다.

    [Fact]
    public void AutoFitWidth_TakesTheWidestCell()
    {
        // 가장 긴 것이 잘리지 않아야 하므로 최댓값이다. 여유는 삼컴의 반올림이
        // 마지막 글자를 … 로 바꾸는 것을 막는다.
        Assert.Equal(202, ColumnSync.AutoFitWidth([120, 200, 80]));
    }

    [Fact]
    public void AutoFitWidth_WithNothingOnScreen_ChangesNothing()
    {
        // 빈 폴더다. 잴 것이 없으면 폭을 건드리지 않는다 — 0 으로 접으면 컬럼이
        // 사라지고 되돌릴 손잡이도 함께 사라진다.
        Assert.Null(ColumnSync.AutoFitWidth([]));
    }

    [Fact]
    public void AutoFitWidth_NeverGoesBelowTheGrip()
    {
        // 손잡이를 잡을 수 있는 폭은 남긴다 (Moved 와 같은 하한).
        Assert.Equal(32, ColumnSync.AutoFitWidth([4, 10]));
    }

    [Fact]
    public void AutoFitWidth_IgnoresValuesThatAreNotRealWidths()
    {
        // 레이아웃 전이거나 측정이 무한대로 돌아온 셀이다. 그것이 최댓값이 되면
        // 컬럼이 화면 밖으로 나간다.
        Assert.Equal(102, ColumnSync.AutoFitWidth([double.NaN, double.PositiveInfinity, 100]));
    }

    [Fact]
    public void AutoFitWidth_WithNothingMeasurable_ChangesNothing()
    {
        Assert.Null(ColumnSync.AutoFitWidth([double.NaN, 0, -5]));
    }
}
