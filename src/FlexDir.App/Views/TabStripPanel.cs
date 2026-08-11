using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 탭 줄의 배치 — 균등 축소와 가로 스크롤 (docs/DESIGN.md §1-1).
/// <para>
/// <b><see cref="ScrollViewer"/> 를 쓰지 않는다.</b> 가로 스크롤을 켠 <c>ScrollViewer</c> 는
/// 내용을 <b>무한 폭</b>으로 측정하고, 그러면 "180~90 사이에서 균등 분배" 가 영원히 발동하지
/// 않는다 — 패널이 자기에게 몇 px 이 주어졌는지 모르기 때문이다. 게다가 여기 필요한 스크롤은
/// 스크롤바도 버튼도 없고 휠이 가로이며 활성 탭이 끌려오는 것이라 (docs/DESIGN.md §1-1)
/// 프레임워크에서 얻을 것이 거의 없다. 오프셋을 직접 들고 <see cref="UIElement.ClipToBounds"/>
/// 한다.
/// </para>
/// <para>
/// 판정 둘만 채점한다 — <see cref="Widths"/>(몇 px 씩 나눠 갖나)와 <see cref="Reveal"/>
/// (활성 탭을 보이게 하려면 얼마나 밀어야 하나). 나머지는 그 둘을 부르는 배관이다.
/// </para>
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>탭 높이 24 (docs/DESIGN.md §1-1). 줄 높이 28 에서 위 여백 4 를 뺀 값이다.</summary>
    internal const double TabHeight = 24;

    /// <summary>고정 탭은 아이콘만 남아 28 이다 — 나눠 갖는 자리에 끼지 않는다.</summary>
    internal const double PinnedWidth = 28;

    internal const double MaxTabWidth = 180;

    /// <summary>제목에 남는 자리가 36 인 폭이다. 그 아래로는 줄이지 않고 넘치게 둔다.</summary>
    internal const double MinTabWidth = 90;

    internal const double Gap = 2;

    /// <summary>휠 한 칸이 미는 거리. 탭 하나가 넘어가는 값이다.</summary>
    private const double WheelStep = 120;

    /// <summary>삽입선의 두께 (docs/DESIGN.md §1-1: 2px <c>--accent</c> 세로선).</summary>
    private const double InsertionWidth = 2;

    public static readonly DependencyProperty ActiveTabProperty = DependencyProperty.Register(
        nameof(ActiveTab),
        typeof(object),
        typeof(TabStripPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty InsertionBrushProperty = DependencyProperty.Register(
        nameof(InsertionBrush),
        typeof(Brush),
        typeof(TabStripPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private double[] widths = [];

    private double offset;

    /// <summary>
    /// 마지막으로 끌어온 탭. <b>매 배치마다 끌어오면 휠로 옮겨 놓은 화면이 곧장 되돌아간다</b>
    /// — 끌어오는 것은 활성이 <b>바뀔 때</b>다.
    /// </summary>
    private object? revealed;

    public TabStripPanel()
    {
        // 넘친 탭이 툴바 위로 삐져나오지 않게 한다. 스크롤은 오프셋이고 잘라내는 것은 여기다.
        ClipToBounds = true;
    }

    /// <summary>
    /// 지금 활성인 탭. 바뀌면 그것이 보이는 자리로 끌려온다 (docs/DESIGN.md §1-1).
    /// <para>
    /// 타입이 <see cref="object"/> 인 것은 자식의 <c>DataContext</c> 와 참조로만 맞춰 보기
    /// 때문이다 — 패널이 탭의 속성을 읽을 일이 없다.
    /// </para>
    /// </summary>
    public object? ActiveTab
    {
        get => GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    /// <summary>
    /// 드래그 중 삽입선의 색 (<c>--accent</c>). 색을 코드에 박지 않기 위해 밖에서 받는다
    /// (docs/DESIGN.md §5: 하드코딩 색은 없다).
    /// </summary>
    public Brush? InsertionBrush
    {
        get => (Brush?)GetValue(InsertionBrushProperty);
        set => SetValue(InsertionBrushProperty, value);
    }

    /// <summary>
    /// 탭마다 몇 px 인가 (docs/DESIGN.md §1-1). 고정 탭은 <see cref="PinnedWidth"/> 를 먼저
    /// 가져가고, 남은 자리를 비고정 탭이 <see cref="MinTabWidth"/>~<see cref="MaxTabWidth"/>
    /// 사이에서 균등하게 나눈다.
    /// <para>
    /// <b>최소에서 멈추고 넘치게 둔다.</b> 무한 축소(크롬)는 페인 최소 너비 320 에서 글자가
    /// 즉시 사라진다 — 넘치는 쪽은 가로 스크롤이 받는다.
    /// </para>
    /// </summary>
    /// <param name="pinned">탭마다 고정인가. 순서가 곧 그리는 순서다 (<c>StripTabs</c>).</param>
    /// <param name="available">줄에 주어진 폭. 제약이 없으면 답은 최대다.</param>
    internal static double[] Widths(IReadOnlyList<bool> pinned, double available)
    {
        ArgumentNullException.ThrowIfNull(pinned);

        if (pinned.Count == 0)
        {
            return [];
        }

        var pins = pinned.Count(tab => tab);
        var loose = pinned.Count - pins;
        var each = MaxTabWidth;

        if (loose > 0 && !double.IsPositiveInfinity(available))
        {
            var room = available - ((pinned.Count - 1) * Gap) - (pins * PinnedWidth);

            each = Math.Clamp(room / loose, MinTabWidth, MaxTabWidth);
        }

        return [.. pinned.Select(tab => tab ? PinnedWidth : each)];
    }

    /// <summary>
    /// 그 탭이 보이려면 오프셋이 얼마여야 하는가. 이미 보이면 <b>움직이지 않는다</b> —
    /// 갱신마다 끌어당기면 사용자가 옮겨 놓은 화면이 제자리로 돌아간다
    /// (<c>FocusScroll</c> 이 같은 이유로 못 찾은 이름만 다시 본다).
    /// </summary>
    internal static double Reveal(double offset, double start, double width, double viewport)
    {
        // 뷰포트보다 넓은 탭은 어차피 다 못 보인다 — 그때 보여야 하는 쪽은 제목이 있는
        // 왼쪽이다. 페인 최소 320 에서는 일어나지 않지만 판정을 비워 두면 오른쪽 끝이 붙는다.
        if (start < offset || width >= viewport)
        {
            return start;
        }

        var overflow = start + width - (offset + viewport);

        return overflow > 0 ? offset + overflow : offset;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        widths = Widths([.. Children.Cast<UIElement>().Select(IsPinned)], availableSize.Width);

        var extent = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].Measure(new Size(widths[index], TabHeight));

            extent += widths[index] + Gap;
        }

        if (Children.Count > 0)
        {
            extent -= Gap;
        }

        // 넘치는 만큼을 달라고 하지 않는다 — 그러면 줄이 페인 밖으로 자란다. 넘친 것은
        // 오프셋으로 감춘다.
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? extent : Math.Min(extent, availableSize.Width),
            TabHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var starts = new double[Children.Count];
        var active = -1;
        var x = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            starts[index] = x;

            if (ActiveTab is not null && Equals(Content(Children[index]), ActiveTab))
            {
                active = index;
            }

            x += widths[index] + Gap;
        }

        var extent = Children.Count > 0 ? x - Gap : 0;

        if (active >= 0 && !Equals(revealed, ActiveTab))
        {
            revealed = ActiveTab;
            offset = Reveal(offset, starts[active], widths[active], finalSize.Width);
        }

        // 자르는 것은 배치마다 한다 — 탭이 닫히거나 페인이 넓어지면 지금 오프셋이 끝을
        // 넘어가고, 그러면 줄 오른쪽에 빈 자리가 생긴다.
        offset = Math.Clamp(offset, 0, Math.Max(0, extent - finalSize.Width));

        var height = Math.Min(TabHeight, finalSize.Height);

        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].Arrange(new Rect(starts[index] - offset, 0, widths[index], height));
        }

        return finalSize;
    }

    /// <summary>
    /// 드래그 중의 삽입선을 그린다 (docs/DESIGN.md §1-1).
    /// <para>
    /// <b>줄 위에 얹은 오버레이가 아니라 여기서 그린다.</b> 이 패널은
    /// <c>ItemsPanelTemplate</c> 안에 있어 <b>이름 범위가 따로</b>이고, 바깥의 <c>Canvas</c> 는
    /// <c>ElementName</c> 으로 이것을 찾을 수 없다 — 자리를 아는 유일한 곳이 여기다.
    /// </para>
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        var x = TabDragInput.GetInsertionX(this);

        if (InsertionBrush is not { } brush || x < 0)
        {
            return;
        }

        // 자리는 틈의 <b>가운데</b>다 — 선의 두께 절반만큼 왼쪽에서 시작한다.
        drawingContext.DrawRectangle(
            brush,
            pen: null,
            new Rect(x - (InsertionWidth / 2), 0, InsertionWidth, Math.Min(TabHeight, ActualHeight)));
    }

    /// <summary>
    /// 탭 줄 위의 휠은 <b>가로</b>다 (docs/DESIGN.md §1-1) — 그것이 탐색기와 같다.
    /// 스크롤 버튼(<c>‹ ›</c>)을 두지 않는 대신 여기가 유일한 손동작이다.
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        offset -= e.Delta / Mouse.MouseWheelDeltaForOneLine * WheelStep;

        InvalidateArrange();

        // 넘칠 것이 없어도 삼킨다 — 여기서 흘려보내면 탭 줄 위의 휠이 아래 목록을 굴린다.
        e.Handled = true;
    }

    private static bool IsPinned(UIElement child) => Content(child) is PaneViewModel { IsPinned: true };

    private static object? Content(UIElement child) => (child as FrameworkElement)?.DataContext;
}
