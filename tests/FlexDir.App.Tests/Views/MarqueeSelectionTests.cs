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
