using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FlexDir.App.Views;

/// <summary>
/// 트리 열과 <c>FolderTreeViewModel</c> 의 폭·접힘을 잇는 attached behavior
/// (docs/PRD-v2.md §10). <see cref="SplitterSync"/> 와 같은 구도이며 다른 점은 셋이다.
/// <list type="bullet">
/// <item>비율이 아니라 <b>픽셀</b>이다 — 비율로 두면 창을 넓힐 때마다 트리가 같이 넓어진다.</item>
/// <item>접으면 열 자체가 <b>0 이 된다</b>. <c>Visibility</c> 만으로는 열이 남아 왼쪽에 빈
/// 띠가 생기고, 그러면 가로 공간을 되찾지 못한다.</item>
/// <item>열에 <c>MinWidth</c> 를 걸지 않는다. 걸면 접을 때 0 으로 못 간다 — 하한은
/// ViewModel 이 자르고(<c>FolderTreeViewModel.Width</c>), 놓는 순간 그 값이 되돌아와
/// 스냅한다.</item>
/// </list>
/// <para>
/// 판정(<see cref="WidthsFor"/>)만 채점하고 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class TreeSync
{
    /// <summary>쓸 수 없는 폭이 왔을 때 펴는 값. <c>GlobalViewState.DefaultTreeWidth</c> 와 같다.</summary>
    internal const double DefaultWidth = 220;

    /// <summary>트리와 페인 사이의 끌 자리. 페인 사이 스플리터와 같은 6 이다.</summary>
    private const double SplitterWidth = 6;

    public static readonly DependencyProperty WidthProperty = DependencyProperty.RegisterAttached(
        "Width",
        typeof(double),
        typeof(TreeSync),
        new FrameworkPropertyMetadata(
            DefaultWidth, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShapeChanged));

    public static readonly DependencyProperty VisibleProperty = DependencyProperty.RegisterAttached(
        "Visible",
        typeof(bool),
        typeof(TreeSync),
        new PropertyMetadata(true, OnShapeChanged));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(TreeSync),
        new PropertyMetadata(false, OnEnabledChanged));

    public static double GetWidth(DependencyObject element) => (double)element.GetValue(WidthProperty);

    public static void SetWidth(DependencyObject element, double value) => element.SetValue(WidthProperty, value);

    public static bool GetVisible(DependencyObject element) => (bool)element.GetValue(VisibleProperty);

    public static void SetVisible(DependencyObject element, bool value) => element.SetValue(VisibleProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 트리 열과 그 옆 스플리터 열의 너비. 접혀 있으면 둘 다 0 이다.
    /// 쓸 수 없는 값(레이아웃 전의 0, NaN)이면 기본 폭으로 편다 — 그대로 펴면 트리가
    /// 사라진 채로 뜬다.
    /// </summary>
    internal static (GridLength Tree, GridLength Splitter) WidthsFor(double width, bool visible)
    {
        if (!visible)
        {
            return (new GridLength(0), new GridLength(0));
        }

        var usable = double.IsFinite(width) && width > 0 ? width : DefaultWidth;

        return (new GridLength(usable), new GridLength(SplitterWidth));
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        grid.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((sender, args) =>
            {
                // 페인 안의 스크롤바 썸도 이 이벤트를 여기까지 올린다 (SplitterSync 가
                // 값을 치르고 배운 것 — 그 판정을 그대로 쓴다).
                if (sender is Grid dragged && SplitterSync.IsSplitterDrag(args.OriginalSource))
                {
                    SetWidth(dragged, dragged.ColumnDefinitions[0].ActualWidth);
                }
            }));

        // 복원이 Loaded 보다 먼저 온다 — 그때의 변경은 아래 콜백이 건너뛰므로 여기서 받아 적는다.
        grid.Loaded += (_, _) => Apply(grid);
    }

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 레이아웃 전에는 펴지 않는다 — XAML 파싱 중에는 열 정의가 아직 비어 있다.
        if (d is Grid { IsLoaded: true } grid)
        {
            Apply(grid);
        }
    }

    private static void Apply(Grid grid)
    {
        var (tree, splitter) = WidthsFor(GetWidth(grid), GetVisible(grid));

        grid.ColumnDefinitions[0].Width = tree;
        grid.ColumnDefinitions[1].Width = splitter;
    }
}
