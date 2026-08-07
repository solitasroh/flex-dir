using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FlexDir.App.Views;

/// <summary>
/// GridSplitter 와 <c>WorkspaceViewModel.SplitterRatio</c> 를 잇는 attached behavior
/// (phase B-2 · docs/DESIGN.md §1).
/// <para>
/// GridSplitter 는 열 정의를 직접 바꾸므로 ViewModel 은 드래그를 모른다. 드래그가 끝나면
/// 실측 너비를 비율로 접어 <see cref="RatioProperty"/> 로 보내고 (양방향 바인딩이 VM 에
/// 전달), VM 의 비율이 바뀌면 (복원·클램프) 열 너비로 편다. 페인 그리드는 좌(0)·스플리터
/// (1)·우(2) 세 열이다.
/// </para>
/// <para>
/// <see cref="EnabledProperty"/> 가 따로 있는 이유: 비율이 기본값(0.5)과 같으면
/// <see cref="RatioProperty"/> 의 변경 콜백이 한 번도 불리지 않아 드래그 훅이 걸리지
/// 않는다. 판정(<see cref="RatioOf"/>·<see cref="WidthsFor"/>)만 채점하고 이벤트 훅은
/// 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class SplitterSync
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.RegisterAttached(
        "Ratio",
        typeof(double),
        typeof(SplitterSync),
        new FrameworkPropertyMetadata(
            0.5, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRatioChanged));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(SplitterSync),
        new PropertyMetadata(false, OnEnabledChanged));

    public static double GetRatio(DependencyObject element) => (double)element.GetValue(RatioProperty);

    public static void SetRatio(DependencyObject element, double value) => element.SetValue(RatioProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 실측 너비 둘을 좌 페인 비율로 접는다. 쓸 수 없는 값(레이아웃 전의 0, NaN)이면
    /// 절반이다 — 깨진 비율을 VM 으로 보내면 저장 파일까지 간다.
    /// </summary>
    internal static double RatioOf(double left, double right)
    {
        var ratio = left / (left + right);

        return double.IsFinite(ratio) ? ratio : 0.5;
    }

    /// <summary>
    /// 이 드래그가 스플리터의 것인가. <c>Thumb.DragCompleted</c> 는 버블링이라 <b>페인 안의
    /// 스크롤바 썸</b>이 올린 것도 같은 핸들러에 닿는다 (실측 2026-08-07 · ScrollBar 는 그
    /// 이벤트를 삼키지 않는다).
    /// <para>
    /// 그것을 비율 변경으로 읽으면 안 되는 이유: 열에는 <c>MinWidth</c> 가 걸려 있어서 실측
    /// 폭이 <b>사용자가 고른 비율이 아니라 벽에 막힌 폭</b>일 수 있다. 좁은 창에서 목록을 한 번
    /// 스크롤하는 것만으로 그 값이 정본이 되고 저장 파일까지 간다 — 넓은 창으로 돌아와도
    /// 원래 자리로 오지 않는다.
    /// </para>
    /// </summary>
    internal static bool IsSplitterDrag(object? source) => source is GridSplitter;

    /// <summary>비율을 좌·우 열의 star 너비로 편다. star 끼리라 합이 1 이 아니어도 좋다.</summary>
    internal static (GridLength Left, GridLength Right) WidthsFor(double ratio)
        => (new GridLength(ratio, GridUnitType.Star), new GridLength(1 - ratio, GridUnitType.Star));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        grid.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((sender, e) =>
            {
                if (sender is Grid g && IsSplitterDrag(e.OriginalSource))
                {
                    // 실측(ActualWidth)을 쓴다. Width 는 드래그 결과의 단위가 구현 소관이라
                    // star 값인지 픽셀인지 약속이 없다.
                    SetRatio(g, RatioOf(g.ColumnDefinitions[0].ActualWidth, g.ColumnDefinitions[2].ActualWidth));
                }
            }));

        // 복원이 Loaded 보다 먼저 온다 (창을 만들고 나서 보이기 전에 복원한다) — 그때의
        // 비율 변경은 아래 콜백이 건너뛰므로 여기서 받아 적는다.
        grid.Loaded += (_, _) => Apply(grid, GetRatio(grid));
    }

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 레이아웃 전에는 펴지 않는다 — XAML 파싱 중에는 열 정의가 아직 비어 있다.
        if (d is Grid { IsLoaded: true } grid)
        {
            Apply(grid, (double)e.NewValue);
        }
    }

    private static void Apply(Grid grid, double ratio)
    {
        var (left, right) = WidthsFor(ratio);

        grid.ColumnDefinitions[0].Width = left;
        grid.ColumnDefinitions[2].Width = right;
    }
}
