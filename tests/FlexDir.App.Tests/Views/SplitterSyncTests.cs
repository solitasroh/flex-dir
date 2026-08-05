using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// GridSplitter 와 <c>SplitterRatio</c> 를 잇는 attached behavior (phase B-2).
/// <para>
/// GridSplitter 는 열 정의를 직접 바꾸므로 ViewModel 이 모른다. 드래그가 끝나면 실측
/// 너비를 비율로 접어 VM 으로 보내고, VM 의 비율(복원·클램프)이 바뀌면 열 너비로 편다 —
/// 비율 계산과 너비 변환만 여기서 채점하고, 이벤트 훅과 실제 드래그는 사람이 확인한다
/// (CLAUDE.md §5).
/// </para>
/// </summary>
public class SplitterSyncTests
{
    // ── 실측 너비 → 비율 판정 ─────────────────────────────────────

    [Theory]
    [InlineData(700, 300, 0.7)]
    [InlineData(320, 320, 0.5)]
    [InlineData(1, 3, 0.25)]
    public void RatioOf_FoldsTheMeasuredWidthsIntoARatio(double left, double right, double expected)
    {
        Assert.Equal(expected, SplitterSync.RatioOf(left, right), precision: 10);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 300)]
    [InlineData(double.PositiveInfinity, 300)]
    public void RatioOf_WhenTheWidthsAreNotUsable_FallsBackToHalf(double left, double right)
    {
        // 레이아웃 전(0)이나 깨진 값에서 비율을 만들면 그 값이 VM 을 거쳐 저장 파일까지 간다.
        Assert.Equal(0.5, SplitterSync.RatioOf(left, right));
    }

    // ── 비율 → 열 너비 변환 ───────────────────────────────────────

    [Fact]
    public void WidthsFor_SplitsTheStarsByTheRatio()
    {
        var (left, right) = SplitterSync.WidthsFor(0.3);

        Assert.Equal(new GridLength(0.3, GridUnitType.Star), left);
        Assert.Equal(new GridLength(0.7, GridUnitType.Star), right);
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Ratio_RoundTrips()
    {
        var element = new DependencyObject();

        SplitterSync.SetRatio(element, 0.3);

        Assert.Equal(0.3, SplitterSync.GetRatio(element));
    }

    [Fact]
    public void Ratio_DefaultsToHalf()
    {
        Assert.Equal(0.5, SplitterSync.GetRatio(new DependencyObject()));
    }

    [Fact]
    public void Enabled_RoundTripsAndDefaultsToOff()
    {
        var element = new DependencyObject();

        Assert.False(SplitterSync.GetEnabled(element));

        SplitterSync.SetEnabled(element, true);

        Assert.True(SplitterSync.GetEnabled(element));
    }
}
