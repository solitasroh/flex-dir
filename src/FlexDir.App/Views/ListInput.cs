using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>클릭 하나가 뜻하는 조작 (docs/DESIGN.md §9).</summary>
public enum ListClick
{
    Select,
    Toggle,
    Range,
}

/// <summary>
/// 목록의 마우스 입력을 ViewModel 커맨드로 넘기는 attached behavior.
/// <para>
/// 행 템플릿의 <c>MouseBinding</c> 은 시각 트리 밖이라 페인 커맨드에 바인딩할 수 없다
/// (Freezable 은 자기 DataContext, 즉 행만 안다). 그래서 목록 컨트롤에 커맨드 다섯을 걸고,
/// 눌린 자리의 컨테이너를 찾아 항목을 넘긴다. 코드비하인드 금지(CLAUDE.md §2) 아래에서
/// 마우스가 ViewModel 에 닿는 유일한 통로다 — 판단은 <see cref="Choose"/> 하나뿐이고
/// 그것만 채점된다.
/// </para>
/// </summary>
public static class ListInput
{
    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.RegisterAttached(
            "SelectCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null, OnWired));

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.RegisterAttached(
            "ToggleCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static readonly DependencyProperty RangeCommandProperty =
        DependencyProperty.RegisterAttached(
            "RangeCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static readonly DependencyProperty OpenCommandProperty =
        DependencyProperty.RegisterAttached(
            "OpenCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static readonly DependencyProperty EmptyCommandProperty =
        DependencyProperty.RegisterAttached(
            "EmptyCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static ICommand? GetSelectCommand(DependencyObject element) => (ICommand?)element.GetValue(SelectCommandProperty);

    public static void SetSelectCommand(DependencyObject element, ICommand? value) => element.SetValue(SelectCommandProperty, value);

    public static ICommand? GetToggleCommand(DependencyObject element) => (ICommand?)element.GetValue(ToggleCommandProperty);

    public static void SetToggleCommand(DependencyObject element, ICommand? value) => element.SetValue(ToggleCommandProperty, value);

    public static ICommand? GetRangeCommand(DependencyObject element) => (ICommand?)element.GetValue(RangeCommandProperty);

    public static void SetRangeCommand(DependencyObject element, ICommand? value) => element.SetValue(RangeCommandProperty, value);

    public static ICommand? GetOpenCommand(DependencyObject element) => (ICommand?)element.GetValue(OpenCommandProperty);

    public static void SetOpenCommand(DependencyObject element, ICommand? value) => element.SetValue(OpenCommandProperty, value);

    public static ICommand? GetEmptyCommand(DependencyObject element) => (ICommand?)element.GetValue(EmptyCommandProperty);

    public static void SetEmptyCommand(DependencyObject element, ICommand? value) => element.SetValue(EmptyCommandProperty, value);

    /// <summary>
    /// 수정키 → 조작. Shift 가 Ctrl 보다 세다 (탐색기와 같다 — Ctrl+Shift 클릭은 범위 선택).
    /// 모르는 수정키는 평 클릭으로 다룬다.
    /// </summary>
    internal static ListClick Choose(ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            return ListClick.Range;
        }

        return modifiers.HasFlag(ModifierKeys.Control) ? ListClick.Toggle : ListClick.Select;
    }

    /// <summary>
    /// <c>SelectCommand</c> 가 걸리는 순간 이벤트를 훅한다. 목록마다 한 번이다 — 커맨드를
    /// 바꿔 끼우는 조작은 없으므로 해제 경로를 만들지 않는다.
    /// </summary>
    private static void OnWired(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list || args.OldValue is not null)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown += OnMouseDown;
        list.MouseDoubleClick += OnDoubleClick;
    }

    /// <summary>
    /// 눌린 자리의 항목. 빈 곳이면 null 이다.
    /// <para>
    /// 컨테이너를 묻지 않고 <c>DataContext</c> 를 거슬러 올라가는 이유: wrap 뷰 3종에서
    /// 목록의 컨테이너는 <b>행</b>이다 (ADR-016). 거기서 멈추면 클릭이 항목에 닿지 않고
    /// 커맨드가 받는 타입도 아니다. 마지막 줄의 남은 칸에서는 행만 만나고 끝나므로
    /// 빈 곳으로 판정된다 — 선택이 풀리는 것이 맞다 (docs/DESIGN.md §9-1).
    /// </para>
    /// </summary>
    internal static FileItemViewModel? ItemAt(DependencyObject list, DependencyObject? origin)
    {
        var node = origin;

        while (node is not null && node != list)
        {
            if (node is FrameworkElement { DataContext: FileItemViewModel item })
            {
                return item;
            }

            // 시각 요소가 아니면 더 갈 수 없다 (GetParent 가 던진다). 우리 템플릿에는 없다.
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return null;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var list = (ItemsControl)sender;

        // 행 컨테이너는 포커스를 받지 않으므로 (내장 선택 배제) 클릭이 키보드 포커스를
        // 옮겨 주지 않는다. 여기서 목록에 준다 — 주소줄에 남으면 방향키가 주소를 편집한다.
        if (list.Focusable)
        {
            list.Focus();
        }

        var item = ItemAt(list, args.OriginalSource as DependencyObject);

        if (item is null)
        {
            GetEmptyCommand(list)?.Execute(null);
            return;
        }

        var command = Choose(Keyboard.Modifiers) switch
        {
            ListClick.Toggle => GetToggleCommand(list),
            ListClick.Range => GetRangeCommand(list),
            _ => GetSelectCommand(list),
        };

        command?.Execute(item);
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs args)
    {
        // 수정키를 누른 더블클릭은 열기가 아니다 — Ctrl 클릭 두 번은 토글 두 번이다.
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var list = (ItemsControl)sender;

        if (ItemAt(list, args.OriginalSource as DependencyObject) is { } item)
        {
            GetOpenCommand(list)?.Execute(item);
            args.Handled = true;
        }
    }
}
