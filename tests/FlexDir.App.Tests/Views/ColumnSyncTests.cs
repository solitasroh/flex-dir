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
}
