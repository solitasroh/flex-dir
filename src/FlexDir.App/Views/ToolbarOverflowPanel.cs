using System.Windows;
using System.Windows.Controls;

namespace FlexDir.App.Views;

/// <summary>
/// 툴바의 접기 배치 — 폭이 모자라면 덜 중요한 버튼 무리를 접고 <c>»</c> 로 보낸다
/// (사용자 확정 2026-08-21: ① 외부도구 → ② 분류 → ③ 뷰 모드 순서로, 이동·새로고침·📍 는
/// 절대 접지 않는다).
/// <para>
/// <b>패널은 접기만 한다 — 살아 있는 자식을 <c>»</c> 메뉴로 옮기지 않는다.</b> WPF 의 논리
/// 트리 부모는 하나뿐이라 옮기면 스타일·바인딩이 재평가되며 깨진다. <c>»</c> 안의 서브메뉴는
/// XAML 이 따로 선언하고 <see cref="FoldedOrders"/> 를 보고 자기를 보일지 정한다
/// (<c>FoldedOrderToVisibility</c>).
/// </para>
/// <para>
/// <b><c>ScrollViewer</c> 를 쓰지 않는다</b> — 가로 스크롤을 켠 <c>ScrollViewer</c> 는 내용을
/// 무한 폭으로 측정해 "모자란가" 판정이 영원히 발동하지 않는다 (<c>TabStripPanel</c> 과 같은
/// 이유). <b><c>DockPanel</c> 로 오른쪽 정렬을 풀지도 않는다</b> — <c>Dock=Right</c> 자식을
/// 먼저 재는데 <c>»</c> 가 보일지는 접기를 판정한 뒤에야 정해져 한 프레임 늦게 진동한다.
/// 오른쪽 정렬까지 이 패널이 쥔다 (<see cref="AlignRightProperty"/>).
/// </para>
/// <para>
/// 판정 하나만 채점한다 — <see cref="Folded"/>(어느 무리가 접히는가). 나머지는 그것을 부르는
/// 배관이다 (<c>TabStripPanel</c> 과 같은 수).
/// </para>
/// </summary>
public sealed class ToolbarOverflowPanel : Panel
{
    /// <summary>
    /// 접히는 순번. 0(기본)은 절대 안 접힌다. 1 부터는 작은 번호가 먼저 접힌다.
    /// 같은 번호는 한 덩어리로 함께 접힌다 — 구분자 <c>Rectangle</c> 도 앞 무리와 같은 번호를
    /// 준다 (하나만 사라지면 남은 버튼이 무슨 뜻인지 알 수 없고 구분자가 고아로 남는다).
    /// </summary>
    public static readonly DependencyProperty FoldOrderProperty = DependencyProperty.RegisterAttached(
        "FoldOrder",
        typeof(int),
        typeof(ToolbarOverflowPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>
    /// 이 자식이 <c>»</c> 머리인가. 접힌 것이 있을 때만 보이고, 그 폭만큼 예산에서 뺀다.
    /// </summary>
    public static readonly DependencyProperty IsOverflowHeadProperty = DependencyProperty.RegisterAttached(
        "IsOverflowHead",
        typeof(bool),
        typeof(ToolbarOverflowPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>
    /// 오른쪽 끝에 붙는가. 알려진 폴더 📍 와 <c>»</c> 머리가 이것을 켠다.
    /// 켠 자식끼리는 선언 순서대로 왼쪽에서 오른쪽으로 놓인다.
    /// </summary>
    public static readonly DependencyProperty AlignRightProperty = DependencyProperty.RegisterAttached(
        "AlignRight",
        typeof(bool),
        typeof(ToolbarOverflowPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private static readonly DependencyPropertyKey FoldedOrdersKey = DependencyProperty.RegisterReadOnly(
        nameof(FoldedOrders),
        typeof(IReadOnlyList<int>),
        typeof(ToolbarOverflowPanel),
        new FrameworkPropertyMetadata(Array.Empty<int>()));

    /// <summary>
    /// 지금 접혀 있는 <c>FoldOrder</c> 들. <c>»</c> 안의 서브메뉴가 이것을 보고 자기를 보일지
    /// 정한다. 의존 속성인 것은 <c>ElementName</c> 바인딩이 변경을 들어야 해서다 — 값이 실제로
    /// 바뀔 때만 새 인스턴스를 넣는다 (매 측정마다 넣으면 바인딩이 매 프레임 재평가된다).
    /// </summary>
    public static readonly DependencyProperty FoldedOrdersProperty = FoldedOrdersKey.DependencyProperty;

    /// <summary>
    /// 자식마다의 자연 폭·높이. <b><see cref="Visibility.Collapsed"/> 인 자식은 측정이
    /// (0,0) 이라</b> 접기 전에 잰 값을 여기서 다시 쓴다 — 0 을 자연 폭으로 읽으면 "자리가
    /// 남네" 로 펼치고, 펼치면 다시 접고, 무한 진동한다.
    /// </summary>
    private readonly Dictionary<UIElement, Size> natural = [];

    public ToolbarOverflowPanel()
    {
        // 두 무리가 겹칠 만큼 좁을 때 잘려 나간 것이 툴바 밖으로 삐져나오지 않게 한다.
        ClipToBounds = true;
    }

    /// <inheritdoc cref="FoldedOrdersProperty"/>
    public IReadOnlyList<int> FoldedOrders => (IReadOnlyList<int>)GetValue(FoldedOrdersProperty);

    public static int GetFoldOrder(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (int)element.GetValue(FoldOrderProperty);
    }

    public static void SetFoldOrder(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(FoldOrderProperty, value);
    }

    public static bool GetIsOverflowHead(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(IsOverflowHeadProperty);
    }

    public static void SetIsOverflowHead(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IsOverflowHeadProperty, value);
    }

    public static bool GetAlignRight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(AlignRightProperty);
    }

    public static void SetAlignRight(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(AlignRightProperty, value);
    }

    /// <summary>
    /// 어느 자식이 접히는가. 작은 <c>FoldOrder</c> 무리부터 접고, 남은 폭이 예산 이하가 되면
    /// 멈춘다. 접기 시작한 뒤의 예산은 <paramref name="available"/> 에서
    /// <paramref name="overflowWidth"/> 를 뺀 값이다 — <c>»</c> 가 그 자리를 차지한다.
    /// <para>
    /// 접을 무리가 떨어지면 <b>더 못 접고 그대로 넘친다</b> — <c>FoldOrder == 0</c> 은 어떤
    /// 경우에도 접지 않는다. <c>TabStripPanel.Widths</c> 가 "최소에서 멈추고 넘치게 둔다" 를
    /// 고른 것과 같은 판단이다.
    /// </para>
    /// </summary>
    /// <param name="foldOrders">자식마다의 <c>FoldOrder</c>. <c>»</c> 머리는 포함하지 않는다.</param>
    /// <param name="widths">자식마다의 자연 폭. <paramref name="foldOrders"/> 와 같은 길이·같은 순서.</param>
    /// <param name="available">줄에 주어진 폭. 무한이면 아무것도 접지 않는다.</param>
    /// <param name="overflowWidth"><c>»</c> 머리의 폭. 하나라도 접히면 이만큼이 예산에서 빠진다.</param>
    /// <returns>자식마다 접혔는가. 길이는 <paramref name="foldOrders"/> 와 같다.</returns>
    internal static bool[] Folded(
        IReadOnlyList<int> foldOrders,
        IReadOnlyList<double> widths,
        double available,
        double overflowWidth)
    {
        ArgumentNullException.ThrowIfNull(foldOrders);
        ArgumentNullException.ThrowIfNull(widths);

        if (foldOrders.Count != widths.Count)
        {
            throw new ArgumentException(
                $"foldOrders({foldOrders.Count})와 widths({widths.Count})의 길이가 다르다.",
                nameof(widths));
        }

        var folded = new bool[foldOrders.Count];
        var remaining = widths.Sum();

        if (double.IsPositiveInfinity(available) || remaining <= available)
        {
            return folded;
        }

        var budget = available - overflowWidth;

        foreach (var order in foldOrders.Where(order => order > 0).Distinct().Order())
        {
            for (var index = 0; index < foldOrders.Count; index++)
            {
                if (foldOrders[index] == order)
                {
                    folded[index] = true;
                    remaining -= widths[index];
                }
            }

            if (remaining <= budget)
            {
                break;
            }
        }

        return folded;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UIElement? head = null;
        var body = new List<UIElement>(InternalChildren.Count);

        foreach (UIElement? child in InternalChildren)
        {
            if (child is null)
            {
                continue;
            }

            if (head is null && GetIsOverflowHead(child))
            {
                head = child;
            }
            else
            {
                body.Add(child);
            }
        }

        var probe = new Size(double.PositiveInfinity, availableSize.Height);
        var orders = new int[body.Count];
        var widths = new double[body.Count];

        for (var index = 0; index < body.Count; index++)
        {
            orders[index] = GetFoldOrder(body[index]);
            widths[index] = NaturalSize(body[index], probe).Width;
        }

        var overflowWidth = head is null ? 0 : NaturalSize(head, probe).Width;
        var folded = Folded(orders, widths, availableSize.Width, overflowWidth);
        var any = Array.Exists(folded, fold => fold);

        for (var index = 0; index < body.Count; index++)
        {
            Show(body[index], !folded[index]);
        }

        // » 머리는 하나라도 접혔을 때만 보인다 — 접힌 것이 없는데 떠 있으면 빈 메뉴다.
        if (head is not null)
        {
            Show(head, any);
        }

        PublishFoldedOrders(orders, folded);

        var extent = 0d;
        var height = 0d;

        for (var index = 0; index < body.Count; index++)
        {
            if (folded[index])
            {
                continue;
            }

            extent += widths[index];
            height = Math.Max(height, natural[body[index]].Height);
        }

        if (head is not null && any)
        {
            extent += overflowWidth;
            height = Math.Max(height, natural[head].Height);
        }

        // 넘치는 만큼을 달라고 하지 않는다 — 그러면 줄이 페인 밖으로 자란다
        // (TabStripPanel.MeasureOverride 와 같은 이유). 넘친 것은 ClipToBounds 가 자른다.
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? extent : Math.Min(extent, availableSize.Width),
            double.IsPositiveInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var right = new List<UIElement>();
        var x = 0d;

        foreach (UIElement? child in InternalChildren)
        {
            // Collapsed 는 배치에서 뺀다 — 화면 밖 좌표로 감추면 히트테스트에 걸린다.
            if (child is null || child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (GetAlignRight(child))
            {
                right.Add(child);

                continue;
            }

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));

            x += child.DesiredSize.Width;
        }

        // 두 무리가 겹칠 만큼 좁으면 왼쪽 무리가 이긴다 — 이동·새로고침이 가려지느니
        // 📍 가 잘리는 쪽이 낫다.
        var start = Math.Max(x, finalSize.Width - right.Sum(child => child.DesiredSize.Width));

        foreach (var child in right)
        {
            child.Arrange(new Rect(start, 0, child.DesiredSize.Width, finalSize.Height));

            start += child.DesiredSize.Width;
        }

        return finalSize;
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        // 자식이 바뀌면 캐시를 비운다 — 지워진 자식의 폭을 들고 있을 이유가 없다.
        natural.Clear();
    }

    /// <summary>
    /// 자연 크기 — 보이는 자식은 무한 폭으로 재서 캐시하고, <see cref="Visibility.Collapsed"/>
    /// 인 자식은 캐시를 그대로 쓴다 (다시 재면 0 이 나와 진동한다). 접힌 채 처음 보는
    /// 자식(<c>»</c> 머리가 접혀 시작한 경우)은 한 번 펴서 잰다 — 어차피 접기 판정이 이 패스
    /// 끝에 <see cref="Visibility"/> 를 다시 정한다.
    /// </summary>
    private Size NaturalSize(UIElement child, Size probe)
    {
        if (child.Visibility == Visibility.Collapsed && natural.TryGetValue(child, out var cached))
        {
            return cached;
        }

        if (child.Visibility == Visibility.Collapsed)
        {
            child.Visibility = Visibility.Visible;
        }

        child.Measure(probe);

        return natural[child] = child.DesiredSize;
    }

    private void PublishFoldedOrders(int[] orders, bool[] folded)
    {
        var now = orders.Where((_, index) => folded[index]).Distinct().Order().ToArray();

        if (!now.SequenceEqual(FoldedOrders))
        {
            SetValue(FoldedOrdersKey, now);
        }
    }

    private static void Show(UIElement child, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (child.Visibility != visibility)
        {
            child.Visibility = visibility;
        }
    }
}
