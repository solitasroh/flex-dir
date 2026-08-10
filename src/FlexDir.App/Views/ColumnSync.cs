using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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
/// 판정(<see cref="WidthsFor"/>·<see cref="Moved"/>)만 채점하고 드래그는 사람이 확인한다
/// (CLAUDE.md §5).
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

    /// <summary>손잡이를 잡을 수 있는 최소 폭.</summary>
    private const double MinimumGrip = 32;

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
