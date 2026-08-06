using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 뷰포트 크기를 <c>PaneViewModel.SetViewportSize</c> 로 미는 attached behavior (phase B-3).
/// <para>
/// 열 수를 정하는 쪽은 ViewModel 이고 (ADR-016) View 는 크기만 민다. 쓸 수 있는 크기인가의
/// 판정만 여기서 채점하고, 이벤트 훅과 실제 레이아웃은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class ViewportSyncTests
{
    // ── 쓸 수 있는 크기인가 ───────────────────────────────────────

    [Theory]
    [InlineData(1000, 600)]
    [InlineData(0.5, 0.5)]
    public void IsUsable_AcceptsAFinitePositiveSize(double width, double height)
    {
        Assert.True(ViewportSync.IsUsable(width, height));
    }

    [Theory]
    [InlineData(0, 600)]                       // 레이아웃 전
    [InlineData(1000, 0)]
    [InlineData(-1, 600)]
    [InlineData(double.NaN, 600)]
    [InlineData(1000, double.PositiveInfinity)]  // 무한 제약 아래에서 측정된 값
    public void IsUsable_RejectsWhatWouldMakeABogusColumnCount(double width, double height)
    {
        // 0 을 밀면 줄당 1개로 접혔다가 실제 크기가 와서 다시 묶인다 — 재배치가 한 번 는다.
        // 무한대는 나눗셈이 int 로 접히면서 음수 열 수가 된다.
        Assert.False(ViewportSync.IsUsable(width, height));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Enabled_RoundTripsAndDefaultsToOff()
    {
        var element = new DependencyObject();

        Assert.False(ViewportSync.GetEnabled(element));

        ViewportSync.SetEnabled(element, true);

        Assert.True(ViewportSync.GetEnabled(element));
    }
}
