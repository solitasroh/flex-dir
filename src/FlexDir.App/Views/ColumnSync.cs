using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.Core.ViewState;

namespace FlexDir.App.Views;

/// <summary>
/// Details 헤더의 컬럼 폭과 <c>WorkspaceViewModel</c> 을 잇는 attached behavior
/// (docs/DESIGN.md §2 · 사용자 요청 2026-08-10).
///
/// <para>
/// <b><c>GridSplitter</c> 를 쓰지 않는다.</b> 그것은 언제나 <b>인접한 두 열</b>을 함께
/// 조절하므로 (<c>PreviousAndCurrent</c>) 한 열을 늘리면 옆 열이 줄어드는 시소가 된다.
/// 탐색기는 그렇지 않다 — 경계를 끌면 <b>그 왼쪽 컬럼만</b> 변하고 나머지는 폭을 지킨 채
/// 밀린다. 첫 구현이 이 차이를 놓쳐 "반대로 움직인다" 는 지적을 받았다 (2026-08-10).
/// </para>
///
/// <para>
/// 그래서 <see cref="Thumb"/> 의 <c>DragDelta</c> 를 직접 받아 <b>그 열의 폭에만</b>
/// 움직인 만큼을 더한다. 남는 자리는 헤더 맨 뒤의 빈 열(<c>*</c>)이 먹는다. 어느 열인지는
/// 손잡이의 <c>Tag</c> 가 말한다.
/// </para>
///
/// <para>
/// 행은 이 behavior 를 쓰지 않는다 — 같은 값에 단방향으로 묶여 따라올 뿐이다.
/// 끌 수 있는 것은 헤더뿐이고 정본도 하나여야 한다.
/// </para>
///
/// <para>
/// <b>경계를 더블클릭하면 그 컬럼이 지금 화면에 있는 내용에 맞춰진다</b> (탐색기와 같다 ·
/// 사용자 요청 2026-09-22 — <see cref="AutoFitWidth"/>).
/// </para>
///
/// <para>
/// 판정(<see cref="WidthsFor"/>·<see cref="Moved"/>·<see cref="AutoFitWidth"/>)만 채점하고
/// 드래그와 더블클릭은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class ColumnSync
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(ColumnSync),
        new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty NameWidthProperty = Width(nameof(NameWidthProperty), PaneColumns.Default.Name);

    public static readonly DependencyProperty SizeWidthProperty = Width(nameof(SizeWidthProperty), PaneColumns.Default.Size);

    public static readonly DependencyProperty TypeWidthProperty = Width(nameof(TypeWidthProperty), PaneColumns.Default.Type);

    public static readonly DependencyProperty ModifiedWidthProperty = Width(nameof(ModifiedWidthProperty), PaneColumns.Default.Modified);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static double GetNameWidth(DependencyObject element) => (double)element.GetValue(NameWidthProperty);

    public static void SetNameWidth(DependencyObject element, double value) => element.SetValue(NameWidthProperty, value);

    public static double GetSizeWidth(DependencyObject element) => (double)element.GetValue(SizeWidthProperty);

    public static void SetSizeWidth(DependencyObject element, double value) => element.SetValue(SizeWidthProperty, value);

    public static double GetTypeWidth(DependencyObject element) => (double)element.GetValue(TypeWidthProperty);

    public static void SetTypeWidth(DependencyObject element, double value) => element.SetValue(TypeWidthProperty, value);

    public static double GetModifiedWidth(DependencyObject element) => (double)element.GetValue(ModifiedWidthProperty);

    public static void SetModifiedWidth(DependencyObject element, double value) => element.SetValue(ModifiedWidthProperty, value);

    /// <summary>
    /// 네 열의 너비. <b>넷 다 픽셀이다</b> — 남는 자리는 헤더의 다섯 번째 빈 열이 먹는다.
    /// 쓸 수 없는 값(레이아웃 전의 0, NaN, 음수)은 기본 폭으로 편다.
    /// </summary>
    internal static (GridLength Name, GridLength Size, GridLength Type, GridLength Modified) WidthsFor(
        double name,
        double size,
        double type,
        double modified)
        => (Pixels(name, PaneColumns.Default.Name),
            Pixels(size, PaneColumns.Default.Size),
            Pixels(type, PaneColumns.Default.Type),
            Pixels(modified, PaneColumns.Default.Modified));

    /// <summary>
    /// <b>드래그를 시작할 때의 폭</b>에 그동안 움직인 총 거리를 더한 값. 오른쪽으로 끌면
    /// 넓어진다 — 손잡이는 그 컬럼의 오른쪽 끝이므로 이것이 손이 기대하는 방향이다.
    /// <para>
    /// 증분이 아니라 <b>총 이동량</b>을 받는 것이 요점이다. 증분을 누적하면 손잡이가 따라
    /// 움직이는 만큼이 상쇄돼 방향이 뒤집힌다 (아래 §DragStarted).
    /// </para>
    /// <para>
    /// 하한만 여기서 막는다. 0 이하로 내려가면 손잡이가 사라져 되돌릴 수 없고, 상한은
    /// ViewModel 이 자른다.
    /// </para>
    /// </summary>
    internal static double Moved(double startWidth, double travelled)
    {
        var moved = startWidth + travelled;

        return double.IsFinite(moved) && moved > MinimumGrip ? moved : MinimumGrip;
    }

    /// <summary>
    /// 컬럼을 내용에 맞춘 폭 — 손잡이를 <b>더블클릭</b>했을 때 (사용자 요청 2026-09-22).
    /// <paramref name="contentWidths"/> 는 지금 화면에 있는 셀들의 자연 폭이다.
    /// <para>
    /// <b>가장 넓은 것에 맞춘다</b> — 탐색기와 같다. 그보다 좁히면 맞추려고 누른 그 항목이
    /// 잘린 채로 남는다.
    /// </para>
    /// <para>
    /// <b>잴 것이 없으면 <see langword="null"/> 이고 폭은 그대로 둔다.</b> 빈 폴더에서
    /// 0 으로 접으면 컬럼과 함께 손잡이도 사라져 되돌릴 길이 없어진다.
    /// </para>
    /// <para>
    /// 화면 밖의 항목은 세지 않는다. 가상화 때문에 실현된 행만 잴 수 있기도 하지만,
    /// <b>탐색기의 동작 자체가 그렇다</b> — 스크롤한 자리에서 다시 누르면 그 화면에 맞춘다.
    /// </para>
    /// </summary>
    internal static double? AutoFitWidth(IEnumerable<double> contentWidths)
    {
        ArgumentNullException.ThrowIfNull(contentWidths);

        double? widest = null;

        foreach (var width in contentWidths)
        {
            // 레이아웃 전의 0·NaN, 무한대로 돌아온 측정은 버린다 — 그것이 최댓값이 되면
            // 컬럼이 화면 밖으로 나간다.
            if (double.IsFinite(width) && width > 0 && (widest is not { } max || width > max))
            {
                widest = width;
            }
        }

        return widest is { } found ? Math.Max(found + Slack, MinimumGrip) : null;
    }

    /// <summary>손잡이를 잡을 수 있는 최소 폭.</summary>
    private const double MinimumGrip = 32;

    /// <summary>
    /// 맞춘 폭에 더하는 여유. 소수점 반올림이 마지막 글자를 <c>…</c> 로 바꾸는 것을 막는다.
    /// </summary>
    private const double Slack = 2;

    /// <summary>끄는 중인 컬럼. 손잡이는 한 번에 하나뿐이라 static 으로 충분하다.</summary>
    private static string? dragging;

    private static double dragOriginX;

    private static double dragStartWidth;

    private static double WidthOf(DependencyObject header, string column) => column switch
    {
        "Name" => GetNameWidth(header),
        "Size" => GetSizeWidth(header),
        "Type" => GetTypeWidth(header),
        _ => GetModifiedWidth(header),
    };

    private static void SetWidthOf(DependencyObject header, string column, double width)
    {
        switch (column)
        {
            case "Name":
                SetNameWidth(header, width);
                break;
            case "Size":
                SetSizeWidth(header, width);
                break;
            case "Type":
                SetTypeWidth(header, width);
                break;
            default:
                SetModifiedWidth(header, width);
                break;
        }
    }

    private static DependencyProperty Width(string name, double fallback)
        => DependencyProperty.RegisterAttached(
            name.Replace("Property", string.Empty, StringComparison.Ordinal),
            typeof(double),
            typeof(ColumnSync),
            new FrameworkPropertyMetadata(
                fallback, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWidthChanged));

    private static GridLength Pixels(double value, double fallback)
        => new(double.IsFinite(value) && value > 0 ? value : fallback);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        //
        // **DragDelta 의 HorizontalChange 를 쓰지 않는다.** 그것은 직전 이벤트 대비 증분인데,
        // 폭을 바꾸면 손잡이 자신이 따라 움직여서 다음 증분이 그 이동을 상쇄하는 음수로 온다 —
        // 끄는 방향과 반대로 밀린다 (2026-08-10 실물에서 그랬다). 대신 시작 시점의 폭과
        // 마우스 자리를 기억하고 매번 절대 이동량으로 다시 계산한다. 헤더 Grid 는 움직이지
        // 않으므로 그 기준계는 흔들리지 않는다.
        grid.AddHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler((sender, args) =>
            {
                if (sender is not Grid header || args.OriginalSource is not Thumb { Tag: string column })
                {
                    return;
                }

                dragging = column;
                dragOriginX = Mouse.GetPosition(header).X;
                dragStartWidth = WidthOf(header, column);
            }));

        grid.AddHandler(
            Thumb.DragDeltaEvent,
            new DragDeltaEventHandler((sender, args) =>
            {
                if (sender is not Grid header || dragging is not { } column)
                {
                    return;
                }

                SetWidthOf(header, column, Moved(dragStartWidth, Mouse.GetPosition(header).X - dragOriginX));

                Apply(header);
                args.Handled = true;
            }));

        grid.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => dragging = null));

        // 경계 더블클릭 = 내용에 맞추기 (탐색기와 같다 · 사용자 요청 2026-09-22).
        //
        // 터널링이어야 한다 — 손잡이가 버블링 단계에서 마우스를 잡아 드래그로 삼킨다.
        // 여기서 Handled 로 접지 않으면 맞추자마자 폭이 두 번째 클릭의 드래그를 따라간다.
        grid.PreviewMouseLeftButtonDown += (sender, args) =>
        {
            if (args.ClickCount != 2
                || sender is not Grid header
                || GripAt(args.OriginalSource as DependencyObject, header) is not { Tag: string column })
            {
                return;
            }

            AutoFit(header, column);
            args.Handled = true;
        };

        // 복원이 Loaded 보다 먼저 온다 — 그때의 변경은 아래 콜백이 건너뛴다.
        grid.Loaded += (_, _) => Apply(grid);
    }

    private static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 레이아웃 전에는 펴지 않는다 — XAML 파싱 중에는 열 정의가 아직 비어 있다.
        if (d is Grid { IsLoaded: true } grid)
        {
            Apply(grid);
        }
    }

    /// <summary>눌린 자리 아래의 컬럼 손잡이. 손잡이 밖이면 <see langword="null"/>.</summary>
    private static Thumb? GripAt(DependencyObject? origin, Grid header)
    {
        var node = origin;

        while (node is not null && node != header)
        {
            if (node is Thumb grip)
            {
                return grip;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return null;
    }

    /// <summary>
    /// 컬럼을 지금 화면에 있는 내용에 맞춘다 (<see cref="AutoFitWidth"/>).
    /// <para>
    /// 재는 것은 <b>헤더 자신과 실현된 행들</b>이다 — 가상화된 목록에서 잴 수 있는 것이
    /// 그것뿐이고, 탐색기가 맞추는 범위도 화면에 보이는 것까지다.
    /// </para>
    /// </summary>
    private static void AutoFit(Grid header, string column)
    {
        var index = ColumnIndex(column);

        var widths = CellWidths(header, index);

        if (ListOf(header) is { } list)
        {
            widths = [.. widths, .. CellWidths(list, index)];
        }

        if (AutoFitWidth(widths) is not { } fitted)
        {
            return;
        }

        SetWidthOf(header, column, fitted);
        Apply(header);
    }

    private static int ColumnIndex(string column) => column switch
    {
        "Name" => 0,
        "Size" => 1,
        "Type" => 2,
        _ => 3,
    };

    /// <summary>
    /// 헤더와 같은 페인의 목록. 둘은 같은 <c>DockPanel</c> 안의 형제다 (MainWindow.xaml).
    /// </summary>
    private static ItemsControl? ListOf(Grid header)
        => VisualTreeHelper.GetParent(header) is { } panel ? FirstList(panel) : null;

    private static ItemsControl? FirstList(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is ListBox list)
            {
                return list;
            }

            if (FirstList(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 한 컬럼에 놓인 셀들의 <b>자연 폭</b> — 잘리지 않고 다 보이려면 필요한 폭이다.
    /// <para>
    /// 행과 헤더는 컬럼 정의가 다섯인 <c>Grid</c> 라는 점이 같다 (MainWindow.xaml) —
    /// 그것으로 셀의 부모를 알아본다. 손잡이는 세지 않는다: 자기 컬럼에 얹혀 있을 뿐이다.
    /// </para>
    /// </summary>
    private static List<double> CellWidths(DependencyObject root, int column)
    {
        var widths = new List<double>();

        Walk(root);

        return widths;

        void Walk(DependencyObject parent)
        {
            if (parent is Grid { ColumnDefinitions.Count: 5 } row)
            {
                var count = VisualTreeHelper.GetChildrenCount(row);

                for (var index = 0; index < count; index++)
                {
                    if (VisualTreeHelper.GetChild(row, index) is FrameworkElement cell
                        && cell is not Thumb
                        && Grid.GetColumn(cell) == column)
                    {
                        widths.Add(NaturalWidth(cell));
                    }
                }
            }

            var children = VisualTreeHelper.GetChildrenCount(parent);

            for (var index = 0; index < children; index++)
            {
                Walk(VisualTreeHelper.GetChild(parent, index));
            }
        }
    }

    /// <summary>
    /// 폭 제한 없이 쟀을 때 이 요소가 바라는 폭. 여백도 포함된다 (<c>DesiredSize</c>).
    /// <para>
    /// 잰 뒤에 <c>InvalidateMeasure</c> 로 되돌린다 — 무한대로 잰 결과가 그대로 남으면
    /// 다음 배치가 그 값을 쓴다.
    /// </para>
    /// </summary>
    private static double NaturalWidth(FrameworkElement cell)
    {
        var height = cell.ActualHeight > 0 ? cell.ActualHeight : double.PositiveInfinity;

        cell.Measure(new Size(double.PositiveInfinity, height));

        var width = cell.DesiredSize.Width;

        cell.InvalidateMeasure();

        return width;
    }

    private static void Apply(Grid grid)
    {
        var (name, size, type, modified) = WidthsFor(
            GetNameWidth(grid), GetSizeWidth(grid), GetTypeWidth(grid), GetModifiedWidth(grid));

        grid.ColumnDefinitions[0].Width = name;
        grid.ColumnDefinitions[1].Width = size;
        grid.ColumnDefinitions[2].Width = type;
        grid.ColumnDefinitions[3].Width = modified;
    }
}
