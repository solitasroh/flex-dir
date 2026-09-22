using System.Windows;
using System.Windows.Input;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

public class MarqueeSelectionTests
{
    [Fact]
    public void Bounds_NormalizesDraggingInAnyDirection()
    {
        Assert.Equal(new Rect(10, 20, 30, 40), MarqueeSelection.Bounds(new Point(40, 60), new Point(10, 20)));
    }

    [Fact]
    public void Clamp_AMarqueeInsideTheList_IsUnchanged()
    {
        Assert.Equal(
            new Rect(10, 20, 30, 40),
            MarqueeSelection.Clamp(new Rect(10, 20, 30, 40), new Size(200, 200)));
    }

    [Fact]
    public void Clamp_AMarqueeRunningPastTheList_StopsAtTheEdge()
    {
        // 끌기는 캐프처를 잡고 있어 포인터가 목록 밖(툴바·창 밖)으로 나가도 계속 온다.
        // 그대로 그리면 어도너는 잘리지 않아서 사각형이 툴바와 브레드크럼을 덮는다
        // (2026-09-22 사용자 신고). 고르는 범위가 아니라 보이는 범위를 자른다.
        Assert.Equal(
            new Rect(50, 60, 150, 140),
            MarqueeSelection.Clamp(new Rect(50, 60, 400, 400), new Size(200, 200)));
    }

    [Fact]
    public void Clamp_AMarqueeStartingBeforeTheList_StopsAtTheEdge()
    {
        Assert.Equal(
            new Rect(0, 0, 30, 40),
            MarqueeSelection.Clamp(new Rect(-20, -30, 50, 70), new Size(200, 200)));
    }

    [Fact]
    public void Clamp_AMarqueeCompletelyOutside_IsEmpty()
    {
        // 시작점이 항상 목록 안이라 실제로는 안 나오지만, 빈 사각형을 그리는 일은
        // 없어야 한다 — Rect.Empty 를 그리면 WPF 가 던져 끌기 전체가 죽는다.
        Assert.True(MarqueeSelection.Clamp(new Rect(300, 300, 50, 50), new Size(200, 200)).IsEmpty);
    }


    [Fact]
    public void HitNames_TakesOnlyItemsIntersectingTheRectangle()
    {
        var items = new[]
        {
            new MarqueeItemBounds("a.txt", new Rect(10, 10, 20, 20)),
            new MarqueeItemBounds("b.txt", new Rect(50, 50, 20, 20)),
        };

        Assert.Equal(["a.txt"], MarqueeSelection.HitNames(items, new Rect(0, 0, 35, 35)));
    }

    [Fact]
    public void Combine_WithoutModifiers_ReplacesTheSelection()
    {
        Assert.Equal(
            ["b.txt", "c.txt"],
            Sorted(MarqueeSelection.Combine(["a.txt"], ["b.txt", "c.txt"], ModifierKeys.None)));
    }

    [Fact]
    public void Combine_WithShift_AddsToTheSelection()
    {
        Assert.Equal(
            ["a.txt", "b.txt"],
            Sorted(MarqueeSelection.Combine(["a.txt"], ["b.txt"], ModifierKeys.Shift)));
    }

    [Fact]
    public void Combine_WithControl_TogglesTheItemsInTheRectangle()
    {
        Assert.Equal(
            ["b.txt", "c.txt"],
            Sorted(MarqueeSelection.Combine(["a.txt", "b.txt"], ["a.txt", "c.txt"], ModifierKeys.Control)));
    }

    private static string[] Sorted(IReadOnlyCollection<string> names)
        => [.. names.Order(StringComparer.OrdinalIgnoreCase)];
}
