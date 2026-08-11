using System.Windows;
using System.Windows.Input;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 탭 드래그 — 같은 페인 안의 순서 바꾸기와 반대편 페인으로 보내기 (docs/PRD-v2.md §17 ·
/// docs/DESIGN.md §1-1 §탭 드래그 중).
/// <para>
/// <b>소유권 이동은 여기 없다.</b> 그것은 <c>PaneTabsViewModel.SendAsync</c> 가 쥐고 이미
/// 채점됐다 — §17 이 *"메뉴가 먼저다, 드래그는 그 위에 입력만 얹는다"* 로 둔 이유가 그것이다.
/// 여기 있는 것은 입력(어디서 시작했나 · 어느 틈에 놓았나)과 표시(삽입선 · 줄 강조 ·
/// 끌리는 탭의 반투명)뿐이다.
/// </para>
/// <para>
/// <b>목록의 파일 드래그와는 시작 자리로 갈린다</b> (docs/PRD-v2.md §17). 그쪽은
/// <c>ListInput</c>·<c>DragDropInput</c> 이 목록에 걸려 있고 이쪽은 탭 줄에 걸린다 —
/// 같은 화면에 둘이 살아 있어도 섞이지 않는다. 탭 위로 <b>파일</b>을 끌어오는 것은 MVP 밖이라
/// (§17 제외 목록) 여기서 <see cref="DragDropEffects.None"/> 으로 거절한다.
/// </para>
/// <para>
/// <c>DragDrop.DoDragDrop</c> 은 UI 스레드에서만 시작할 수 있고 자체 메시지 루프를 돈다 —
/// CLAUDE.md §3 위반이 아니다 (싣는 것이 인스턴스 참조뿐이라 저장소에 닿지 않는다).
/// </para>
/// </summary>
public static class TabDragInput
{
    /// <summary>삽입선이 없다는 뜻. 폭이 0 인 자리와 구분되어야 해서 음수다.</summary>
    internal const double NoInsertion = -1;

    /// <summary>이 앱 안에서만 도는 형식이다 — 다른 프로그램이 이해할 것이 없다.</summary>
    private const string TabFormat = "flex-dir tab";

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(TabDragInput),
        new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>
    /// 삽입선을 그릴 자리 (docs/DESIGN.md §1-1: 2px <c>--accent</c> 세로선).
    /// <b>탭이 실시간으로 밀리지 않는다</b> — 밀면 좁은 페인에서 목표가 계속 움직인다.
    /// </summary>
    public static readonly DependencyProperty InsertionXProperty = DependencyProperty.RegisterAttached(
        "InsertionX",
        typeof(double),
        typeof(TabDragInput),
        new FrameworkPropertyMetadata(NoInsertion, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 반대편 페인에서 온 탭이 이 줄 위에 있다 — <b>줄 전체가 <c>--sel</c></b> 이 된다
    /// (docs/DESIGN.md §1-1). 페인을 건너간다는 것이 줄 단위로 보여야 한다.
    /// </summary>
    public static readonly DependencyProperty IsDropTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropTarget",
        typeof(bool),
        typeof(TabDragInput),
        new PropertyMetadata(false));

    /// <summary>버튼을 누른 자리. 임계값을 넘기 전까지는 드래그가 아니다.</summary>
    private static Point origin;

    private static bool dragging;

    /// <summary>누른 탭의 시각 요소. 끌리는 동안 반투명이 되고 끝나면 되돌린다.</summary>
    private static FrameworkElement? pressed;

    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(EnabledProperty, value);
    }

    public static double GetInsertionX(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (double)element.GetValue(InsertionXProperty);
    }

    public static void SetInsertionX(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(InsertionXProperty, value);
    }

    public static bool GetIsDropTarget(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(IsDropTargetProperty);
    }

    public static void SetIsDropTarget(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IsDropTargetProperty, value);
    }

    /// <summary>
    /// 어느 틈에 놓았는가 — 0 은 첫 탭 앞, <c>tabs.Count</c> 는 마지막 탭 뒤다.
    /// <para>
    /// 경계는 <b>탭의 가운데</b>다. 탭 경계로 잡으면 탭 위 어디에 놓아도 늘 그 왼쪽 틈이라
    /// 오른쪽으로 한 칸 미는 조작이 아예 불가능해진다.
    /// </para>
    /// </summary>
    internal static int GapAt(IReadOnlyList<Rect> tabs, double x)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        return tabs.Count(tab => tab.Left + (tab.Width / 2) < x);
    }

    /// <summary>
    /// 그 틈이 <c>PaneTabsViewModel.MoveTab</c> 에 넘길 몇 번인가.
    /// <para>
    /// <b>앞으로 끌 때만 한 칸 당긴다.</b> 끌던 탭을 목록에서 빼는 순간 그 뒤가 전부 하나씩
    /// 당겨지므로, 보정하지 않으면 놓은 자리보다 한 칸 오른쪽에 선다 — 화면과 결과가 갈린다.
    /// 뒤로 끌 때는 빠지는 자리가 목표보다 뒤라 당겨지는 것이 없다.
    /// </para>
    /// </summary>
    /// <param name="from">끌고 있는 탭이 지금 서 있는 자리 (그리는 순서 기준).</param>
    /// <param name="gap">놓은 틈 (<see cref="GapAt"/>).</param>
    internal static int Target(int from, int gap) => gap > from ? gap - 1 : gap;

    /// <summary>삽입선의 가운데 x. 탭 사이면 간격의 한가운데다.</summary>
    internal static double LineAt(IReadOnlyList<Rect> tabs, int gap)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        if (tabs.Count == 0)
        {
            return 0;
        }

        if (gap <= 0)
        {
            return tabs[0].Left;
        }

        return gap >= tabs.Count
            ? tabs[^1].Right
            : (tabs[gap - 1].Right + tabs[gap].Left) / 2;
    }

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TabStripPanel strip || args.NewValue is not true)
        {
            return;
        }

        strip.AllowDrop = true;

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다
        // (DragDropInput 과 같은 수).
        strip.PreviewMouseLeftButtonDown += OnMouseDown;
        strip.PreviewMouseMove += OnMouseMove;
        strip.DragOver += OnDragOver;
        strip.DragLeave += OnDragLeave;
        strip.Drop += OnDrop;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var strip = (TabStripPanel)sender;

        origin = args.GetPosition(strip);
        pressed = TabStripInput.ChromeAt(strip, args.OriginalSource as DependencyObject);
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        var strip = (TabStripPanel)sender;

        if (args.LeftButton != MouseButtonState.Pressed
            || dragging
            || pressed is not { DataContext: PaneViewModel tab } chrome
            || strip.DataContext is not PaneTabsViewModel tabs
            || !DragDropInput.HasLeftTheStartingPoint(origin, args.GetPosition(strip)))
        {
            return;
        }

        // 원래 자리에 반투명으로 남는다 (docs/DESIGN.md §1-1) — 놓으면 어디로 가는지와
        // 어디서 왔는지가 함께 보인다.
        chrome.Opacity = 0.4;

        // DoDragDrop 은 놓을 때까지 돌아오지 않는다. 재진입을 막지 않으면 그 안에서 오는
        // 마우스 이동이 또 하나를 시작한다.
        dragging = true;

        try
        {
            DragDrop.DoDragDrop(strip, new DataObject(TabFormat, new TabDrag(tabs, tab)), DragDropEffects.Move);
        }
        finally
        {
            dragging = false;
            chrome.Opacity = 1;
            pressed = null;

            // 반대편 줄에 남은 표시는 그쪽의 Drop·DragLeave 가 지운다. 여기는 이 줄 몫이다.
            Clear(strip);
        }
    }

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        var strip = (TabStripPanel)sender;

        args.Handled = true;

        if (Dragged(args) is not { } drag || strip.DataContext is not PaneTabsViewModel tabs)
        {
            // 탭 위 파일 드롭은 MVP 밖이다 (docs/PRD-v2.md §17 제외 목록). 표시를 그리지
            // 않으면 놓아도 아무 일이 없다는 것이 보인다.
            args.Effects = DragDropEffects.None;

            return;
        }

        args.Effects = DragDropEffects.Move;

        if (ReferenceEquals(drag.Source, tabs))
        {
            var bounds = Bounds(strip);

            SetIsDropTarget(strip, false);
            SetInsertionX(strip, LineAt(bounds, GapAt(bounds, args.GetPosition(strip).X)));

            return;
        }

        // 반대편에서 왔다. 어느 틈인지는 묻지 않는다 — 도착 자리는 "그 페인 활성 탭 바로
        // 오른쪽" 으로 정해져 있고 (docs/PRD-v2.md §17), 표시도 줄 단위다.
        SetInsertionX(strip, NoInsertion);
        SetIsDropTarget(strip, true);
    }

    private static void OnDragLeave(object sender, DragEventArgs args) => Clear((TabStripPanel)sender);

    private static void OnDrop(object sender, DragEventArgs args)
    {
        var strip = (TabStripPanel)sender;

        Clear(strip);

        if (Dragged(args) is not { } drag || strip.DataContext is not PaneTabsViewModel tabs)
        {
            return;
        }

        args.Handled = true;

        if (!ReferenceEquals(drag.Source, tabs))
        {
            // 기다리지 않는다 — 드롭 핸들러가 돌아가야 원본의 DoDragDrop 이 풀린다
            // (DragDropInput 과 같은 수). 마지막 탭이면 SendAsync 안에서 거절된다.
            _ = drag.Source.SendAsync(drag.Tab, tabs);

            return;
        }

        var from = IndexOf(strip, drag.Tab);

        if (from >= 0)
        {
            tabs.MoveTab(drag.Tab, Target(from, GapAt(Bounds(strip), args.GetPosition(strip).X)));
        }
    }

    private static TabDrag? Dragged(DragEventArgs args)
        => args.Data.GetDataPresent(TabFormat) ? args.Data.GetData(TabFormat) as TabDrag : null;

    /// <summary>탭들이 지금 차지한 자리. 스크롤 오프셋이 이미 반영된 화면 좌표다.</summary>
    private static Rect[] Bounds(TabStripPanel strip)
        => [.. strip.Children.Cast<UIElement>()
            .Select(child => new Rect(child.TranslatePoint(default, strip), child.RenderSize))];

    /// <summary>그리는 순서에서 몇 번인가. 줄의 자식 순서가 곧 그 순서다 (<c>StripTabs</c>).</summary>
    private static int IndexOf(TabStripPanel strip, PaneViewModel tab)
    {
        for (var index = 0; index < strip.Children.Count; index++)
        {
            if (strip.Children[index] is FrameworkElement { DataContext: { } content } && ReferenceEquals(content, tab))
            {
                return index;
            }
        }

        return -1;
    }

    private static void Clear(TabStripPanel strip)
    {
        SetInsertionX(strip, NoInsertion);
        SetIsDropTarget(strip, false);
    }
}

/// <summary>
/// 끌고 있는 탭과 <b>그것을 쥔 페인</b>. 놓인 자리에서 "같은 페인인가" 를 가르는 것이
/// <see cref="Source"/> 이고, 페인 간 이동일 때 떼어낼 쪽도 그것이다.
/// </summary>
internal sealed record TabDrag(PaneTabsViewModel Source, PaneViewModel Tab);
