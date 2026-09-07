using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

internal readonly record struct MarqueeItemBounds(string Name, Rect Bounds);

/// <summary>
/// 목록의 빈 공간에서 끌어 항목을 고르는 선택 사각형. 항목에서 시작한 끌기는
/// <see cref="DragDropInput"/>이 파일 드래그로 처리한다.
/// </summary>
internal static class MarqueeSelection
{
    private static Session? active;

    internal static Rect Bounds(Point from, Point to)
        => new(
            new Point(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y)),
            new Point(Math.Max(from.X, to.X), Math.Max(from.Y, to.Y)));

    internal static string[] HitNames(IEnumerable<MarqueeItemBounds> items, Rect marquee)
    {
        ArgumentNullException.ThrowIfNull(items);

        return
        [
            .. items
                .Where(item => item.Bounds.IntersectsWith(marquee))
                .Select(item => item.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    internal static IReadOnlyCollection<string> Combine(
        IReadOnlyCollection<string> original,
        IReadOnlyCollection<string> hits,
        ModifierKeys modifiers)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(hits);

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            return original.Concat(hits).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            var toggled = original.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in hits)
            {
                if (!toggled.Remove(name))
                {
                    toggled.Add(name);
                }
            }

            return toggled;
        }

        return hits.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static void Start(ItemsControl list, Point at, PaneSelection selection)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(selection);

        Cancel();
        active = new Session(list, at, selection, [.. selection.SelectedNames]);
    }

    internal static bool Move(
        ItemsControl list,
        Point at,
        MouseButtonState leftButton,
        ModifierKeys modifiers)
    {
        if (active is not { } session || !ReferenceEquals(session.List, list))
        {
            return false;
        }

        if (leftButton != MouseButtonState.Pressed)
        {
            Cancel();
            return false;
        }

        if (!session.Selecting)
        {
            if (!DragDropInput.HasLeftTheStartingPoint(session.Origin, at))
            {
                return false;
            }

            session.Selecting = true;
            ListInput.CancelPendingClick();
            _ = Mouse.Capture(list);

            session.Layer = AdornerLayer.GetAdornerLayer(list);
            session.Adorner = new SelectionAdorner(list, session.Origin, at);
            session.Layer?.Add(session.Adorner);
        }

        session.Adorner?.MoveTo(at);

        var names = HitNames(VisibleItems(list), Bounds(session.Origin, at));
        session.Selection.ReplaceWith(Combine(session.Original, names, modifiers));

        return true;
    }

    internal static bool End(ItemsControl list)
    {
        if (active is not { } session || !ReferenceEquals(session.List, list))
        {
            return false;
        }

        var selected = session.Selecting;
        active = null;
        Clean(session);

        return selected;
    }

    internal static void Cancel(ItemsControl? list = null)
    {
        if (active is not { } session || (list is not null && !ReferenceEquals(session.List, list)))
        {
            return;
        }

        active = null;
        Clean(session);
    }

    private static void Clean(Session session)
    {
        if (session.Adorner is not null)
        {
            session.Layer?.Remove(session.Adorner);
        }

        if (ReferenceEquals(Mouse.Captured, session.List))
        {
            Mouse.Capture(null);
        }
    }

    private static IReadOnlyList<MarqueeItemBounds> VisibleItems(ItemsControl list)
    {
        var found = new Dictionary<string, MarqueeItemBounds>(StringComparer.OrdinalIgnoreCase);
        var areas = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var viewport = new Rect(new Point(), list.RenderSize);

        Walk(list);

        return [.. found.Values];

        void Walk(DependencyObject parent)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);

            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);

                if (child is FrameworkElement
                    {
                        IsVisible: true,
                        DataContext: FileItemViewModel item,
                        ActualWidth: > 0,
                        ActualHeight: > 0,
                    } element)
                {
                    try
                    {
                        var bounds = new Rect(element.TranslatePoint(new Point(), list), element.RenderSize);
                        bounds.Intersect(viewport);
                        var area = bounds.IsEmpty ? 0 : bounds.Width * bounds.Height;

                        if (area > 0 && (!areas.TryGetValue(item.Name, out var previous) || area > previous))
                        {
                            areas[item.Name] = area;
                            found[item.Name] = new MarqueeItemBounds(item.Name, bounds);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 가상화가 방금 컨테이너를 떼었다. 다음 MouseMove에서 다시 센다.
                    }
                }

                Walk(child);
            }
        }
    }

    private sealed class Session(
        ItemsControl list,
        Point origin,
        PaneSelection selection,
        IReadOnlyCollection<string> original)
    {
        public ItemsControl List { get; } = list;
        public Point Origin { get; } = origin;
        public PaneSelection Selection { get; } = selection;
        public IReadOnlyCollection<string> Original { get; } = original;
        public bool Selecting { get; set; }
        public AdornerLayer? Layer { get; set; }
        public SelectionAdorner? Adorner { get; set; }
    }

    private sealed class SelectionAdorner : Adorner
    {
        private readonly Point origin;
        private Point current;

        public SelectionAdorner(UIElement adornedElement, Point origin, Point current)
            : base(adornedElement)
        {
            this.origin = origin;
            this.current = current;
            IsHitTestVisible = false;
        }

        public void MoveTo(Point point)
        {
            current = point;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var accent = (AdornedElement as FrameworkElement)?.TryFindResource("AccentBrush")
                as SolidColorBrush;
            var color = accent?.Color ?? Color.FromRgb(0, 103, 192);
            var fill = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
            var stroke = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));

            fill.Freeze();
            stroke.Freeze();

            drawingContext.DrawRectangle(fill, new Pen(stroke, 1), Bounds(origin, current));
        }
    }
}
