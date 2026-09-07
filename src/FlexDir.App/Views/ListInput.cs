using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using FlexDir.App.ViewModels;

using FlexDir.Core.Operations;

namespace FlexDir.App.Views;

/// <summary>클릭 하나가 뜻하는 조작 (docs/DESIGN.md §9).</summary>
public enum ListClick
{
    Select,
    Toggle,
    Range,
}

/// <summary>
/// 목록의 마우스 입력과 내장 선택이 가로채는 키를 ViewModel 커맨드로 넘기는 attached behavior.
/// <para>
/// 행 템플릿의 <c>MouseBinding</c> 은 시각 트리 밖이라 페인 커맨드에 바인딩할 수 없다
/// (Freezable 은 자기 DataContext, 즉 행만 안다). 그래서 목록 컨트롤에 커맨드를 걸고,
/// 눌린 자리의 컨테이너를 찾아 항목을 넘긴다. 코드비하인드 금지(CLAUDE.md §2) 아래에서
/// 목록 입력이 ViewModel 에 닿는 통로다. 마우스 판정은 <see cref="Choose"/>, 전체 선택 키
/// 판정은 <see cref="IsSelectAllGesture"/>에 모아 채점한다.
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

    public static readonly DependencyProperty RenameCommandProperty =
        DependencyProperty.RegisterAttached(
            "RenameCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static readonly DependencyProperty ContextMenuCommandProperty =
        DependencyProperty.RegisterAttached(
            "ContextMenuCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectAllCommandProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllCommand", typeof(ICommand), typeof(ListInput), new PropertyMetadata(null, OnSelectAllWired));

    private static readonly DependencyProperty SelectAllWiredProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllWired", typeof(bool), typeof(ListInput), new PropertyMetadata(false));

    /// <summary>
    /// 더블클릭을 기다리는 타이머. 마우스는 하나이므로 대기 중인 제스처도 하나다 —
    /// 페인마다 두지 않는다. UI 스레드에서만 만져진다.
    /// </summary>
    private static DispatcherTimer? renameTimer;

    private static ItemsControl? renameList;

    /// <summary>
    /// 마우스를 뗄 때까지 미뤄 둔 평 클릭. 눌린 항목이 이미 선택돼 있으면 지금 선택을
    /// 접지 않는다 — 접으면 여러 개를 끌 수 없다 (탐색기와 같다).
    /// </summary>
    private static DeferredClick? deferred;

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

    public static ICommand? GetRenameCommand(DependencyObject element) => (ICommand?)element.GetValue(RenameCommandProperty);

    public static void SetRenameCommand(DependencyObject element, ICommand? value) => element.SetValue(RenameCommandProperty, value);

    public static ICommand? GetContextMenuCommand(DependencyObject element) => (ICommand?)element.GetValue(ContextMenuCommandProperty);

    public static void SetContextMenuCommand(DependencyObject element, ICommand? value) => element.SetValue(ContextMenuCommandProperty, value);

    public static ICommand? GetSelectAllCommand(DependencyObject element) => (ICommand?)element.GetValue(SelectAllCommandProperty);

    public static void SetSelectAllCommand(DependencyObject element, ICommand? value) => element.SetValue(SelectAllCommandProperty, value);

    /// <summary>
    /// 우클릭이 선택을 바꾸는가. <b>선택 밖을 누를 때만</b>이다 — 여러 개를 고른 뒤 그 위에서
    /// 우클릭하는 것이 일상 조작이라 거기서 선택을 접으면 메뉴가 엉뚱한 대상에 뜬다
    /// (탐색기와 같다).
    /// </summary>
    internal static bool SelectsBeforeMenu(bool hasItem, bool isSelected) => hasItem && !isSelected;

    /// <summary>
    /// 이 클릭이 이름변경 제스처인가 (docs/DESIGN.md §9-1).
    /// <para>
    /// <b>아무것도 바꾸지 않는 클릭</b>일 때만이다 — 수정키가 없고, 첫 클릭이고, 그 항목이
    /// 이미 유일한 선택이고, 목록에 이미 키보드 포커스가 있었을 때. 마지막 조건은 2분할이라
    /// 있다: 반대편 페인을 눌러 활성을 옮기는 것이 일상 조작인데 그때마다 이름변경이 뜨면
    /// 사고가 난다.
    /// </para>
    /// <para>
    /// 여기서 참이어도 곧바로 열지 않는다 — 더블클릭 시간만큼 기다린다. 두 번째 클릭이
    /// 오면 그것은 열기다.
    /// </para>
    /// </summary>
    internal static bool IsRenameGesture(ModifierKeys modifiers, int clicks, bool wasSoleSelection, bool listHadFocus)
        => modifiers == ModifierKeys.None && clicks == 1 && wasSoleSelection && listHadFocus;

    /// <summary>
    /// 선택 확정을 마우스를 뗄 때까지 미룰 것인가.
    /// <para>
    /// 이미 선택된 항목을 평 클릭하면 미룬다 — 여기서 선택을 그 하나로 접으면 여러 개를
    /// 끌 수 없다 (탐색기가 같은 이유로 같은 일을 한다). 선택 밖을 누른 것은 미루지 않는다:
    /// 지금 선택돼야 그것이 끌린다. 수정키가 붙은 클릭도 미루지 않는다 — 선택 자체가
    /// 목적이라 눈에 바로 반응해야 한다.
    /// </para>
    /// </summary>
    internal static bool DefersSelection(ModifierKeys modifiers, int clicks, bool isSelected)
        => Choose(modifiers) == ListClick.Select && clicks == 1 && isSelected;

    /// <summary>
    /// 대기 중인 클릭 후속을 접는다 — 미뤄 둔 선택과 이름변경 타이머 둘 다. 드래그가
    /// 시작되면 그 클릭은 선택 변경도 이름변경도 아니다.
    /// </summary>
    internal static void CancelPendingClick()
    {
        renameTimer?.Stop();
        renameList = null;
        deferred = null;
    }

    /// <summary>
    /// 더블클릭 판정 시간. WPF 가 <c>ClickCount</c> 안에서만 쓰고 밖으로 열지 않아 직접
    /// 묻는다 — <c>SystemParameters.MinimumHorizontalDragDistance</c> 와 같은 성격의 시스템
    /// 메트릭이고, 사용자가 마우스 속도를 바꾸면 따라가야 한다.
    /// </summary>
    /// <remarks>
    /// <c>LibraryImport</c> 가 아니라 <c>DllImport</c> 다. 생성기가 <c>AllowUnsafeBlocks</c>
    /// 를 요구하는데 (SYSLIB1062) 인자도 마샬링도 없는 호출 하나 때문에 <c>FlexDir.App</c>
    /// 전체에 unsafe 를 켤 이유가 없다 — 켜는 것은 정말 interop 을 하는
    /// <c>FlexDir.Shell</c> 뿐이다.
    /// </remarks>
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

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
    /// 목록의 <c>Ctrl+A</c> 인가. 이름변경 편집기 안에서는 <see cref="TextBoxBase"/>가
    /// 자기 글자를 선택해야 하므로 목록이 받지 않는다.
    /// </summary>
    internal static bool IsSelectAllGesture(Key key, ModifierKeys modifiers, bool isEditing)
        => key == Key.A && modifiers == ModifierKeys.Control && !isEditing;

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
        list.PreviewMouseMove += OnMouseMove;
        list.PreviewMouseLeftButtonUp += OnMouseUp;
        list.PreviewMouseRightButtonUp += OnRightButtonUp;
        list.MouseDoubleClick += OnDoubleClick;
        list.LostMouseCapture += OnLostMouseCapture;
    }

    /// <summary><c>SelectAllCommand</c>가 걸리는 순간 자기 키 이벤트를 직접 훅한다.</summary>
    private static void OnSelectAllWired(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list
            || args.NewValue is null
            || (bool)element.GetValue(SelectAllWiredProperty))
        {
            return;
        }

        element.SetValue(SelectAllWiredProperty, true);
        list.PreviewKeyDown += OnKeyDown;
    }

    /// <summary>
    /// <c>PreviewKeyDown</c>에서 받아 <c>ListBox</c>의 내장 키 처리보다 먼저 이름 기반
    /// <see cref="PaneSelection"/>으로 보낸다. 둘은 서로 다른 선택 상태라 내장 선택만 바뀌면
    /// 파일 조작 커맨드가 보는 대상은 바뀌지 않는다.
    /// </summary>
    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        var list = (ItemsControl)sender;

        args.Handled = TrySelectAll(
            list, args.Key, Keyboard.Modifiers, args.OriginalSource as DependencyObject);
    }

    /// <summary>목록의 전체 선택 키를 ViewModel 커맨드로 보냈으면 <see langword="true"/>.</summary>
    internal static bool TrySelectAll(
        ItemsControl list,
        Key key,
        ModifierKeys modifiers,
        DependencyObject? origin)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (!IsSelectAllGesture(key, modifiers, IsEditing(list, origin)))
        {
            return false;
        }

        var command = GetSelectAllCommand(list);

        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <summary>
    /// 우클릭 — 대상을 정하고 컨텍스트 메뉴를 연다. 뗄 때 여는 것이 탐색기와 같다.
    /// </summary>
    private static void OnRightButtonUp(object sender, MouseButtonEventArgs args)
    {
        var list = (ItemsControl)sender;

        // 편집기 안의 우클릭은 TextBox 자신의 메뉴(잘라내기·복사·붙여넣기)다.
        // 그룹 헤더는 항목도 빈 자리도 아니다 — 폴더 메뉴를 띄우지 않는다.
        if (IsEditing(list, args.OriginalSource as DependencyObject)
            || IsGroupHeader(list, args.OriginalSource as DependencyObject))
        {
            return;
        }

        var item = ItemAt(list, args.OriginalSource as DependencyObject);
        var selected = item is not null && SelectionOf(list)?.IsSelected(item.Name) == true;

        if (list.Focusable)
        {
            list.Focus();
        }

        if (SelectsBeforeMenu(item is not null, selected))
        {
            GetSelectCommand(list)?.Execute(item);
        }
        else if (item is null)
        {
            // 빈 곳이면 선택을 풀고 폴더 배경 메뉴를 연다.
            GetEmptyCommand(list)?.Execute(null);
        }

        // shell 은 화면 좌표를 받는다. PointToScreen 이 내는 것이 그 픽셀이다.
        var at = list.PointToScreen(args.GetPosition(list));

        GetContextMenuCommand(list)?.Execute(new ScreenPoint((int)at.X, (int)at.Y));

        args.Handled = true;
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

    /// <summary>
    /// 눌린 자리가 그룹 헤더인가 (docs/PRD-v2.md §6-1).
    /// <para>
    /// 헤더는 항목이 아니라 <see cref="ItemAt"/> 이 <c>null</c> 을 낸다. 그대로 두면
    /// <b>목록의 빈 자리</b>로 판정돼 접으려고 누를 때마다 선택이 통째로 풀린다.
    /// 접기·펴기는 헤더 자신의 버튼이 하므로 목록은 손대지 않고 물러난다.
    /// </para>
    /// </summary>
    internal static bool IsGroupHeader(DependencyObject list, DependencyObject? origin)
    {
        var node = origin;

        while (node is not null && node != list)
        {
            if (node is FrameworkElement { DataContext: DetailRowViewModel { IsHeader: true } })
            {
                return true;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return false;
    }

    /// <summary>
    /// 눌린 자리가 이름변경 편집기 안인가.
    /// <para>
    /// 편집기는 행 템플릿 안에 있으므로 그 클릭이 <b>목록의 터널링 핸들러를 먼저 지난다</b>.
    /// 거기서 목록이 포커스를 가져가면 편집기가 포커스를 잃고, 포커스 상실은 취소다
    /// (docs/DESIGN.md §9-1) — 캐럿을 옮기려고 누른 것뿐인데 편집이 닫힌다.
    /// B-4 실물에서 이 자리를 밟았다.
    /// </para>
    /// </summary>
    internal static bool IsEditing(DependencyObject list, DependencyObject? origin)
    {
        var node = origin;

        while (node is not null && node != list)
        {
            if (node is TextBoxBase)
            {
                return true;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return false;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        var list = (ItemsControl)sender;

        // 편집기 안의 클릭은 캐럿 조작이다. 목록이 손대면 편집이 닫힌다.
        // 그룹 헤더 클릭은 접기·펴기다 — 빈 자리로 보면 누를 때마다 선택이 통째로 풀린다.
        if (IsEditing(list, args.OriginalSource as DependencyObject)
            || IsGroupHeader(list, args.OriginalSource as DependencyObject))
        {
            return;
        }

        // 판정에 쓰는 셋은 클릭이 무엇을 바꾸기 전에 읽어야 한다 — 아래에서 포커스를 옮기고
        // 선택 커맨드를 실행하므로 그 뒤에 물으면 언제나 참이 된다.
        var hadFocus = list.IsKeyboardFocusWithin;
        var item = ItemAt(list, args.OriginalSource as DependencyObject);
        var selection = SelectionOf(list);
        var wasSelected = selection is not null && item is not null && selection.IsSelected(item.Name);
        var wasSole = wasSelected && selection!.Count == 1;

        // 새 클릭은 언제나 지난 클릭의 후속을 접는다.
        CancelPendingClick();

        // 행 컨테이너는 포커스를 받지 않으므로 (내장 선택 배제) 클릭이 키보드 포커스를
        // 옮겨 주지 않는다. 여기서 목록에 준다 — 주소줄에 남으면 방향키가 주소를 편집한다.
        if (list.Focusable)
        {
            list.Focus();
        }

        if (item is null)
        {
            if (selection is not null)
            {
                MarqueeSelection.Start(list, args.GetPosition(list), selection);
            }

            GetEmptyCommand(list)?.Execute(null);
            return;
        }

        MarqueeSelection.Cancel(list);

        if (DefersSelection(Keyboard.Modifiers, args.ClickCount, wasSelected))
        {
            deferred = new DeferredClick(list, item, wasSole, hadFocus);
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

    /// <summary>
    /// 미뤄 둔 클릭이 여기서 확정된다. 끌지 않았으므로 선택을 그 하나로 접는 것이 맞고,
    /// 이름변경 대기도 여기서 시작한다 — 누른 채 끌기 시작한 클릭은 이름변경이 아니다.
    /// </summary>
    private static void OnMouseUp(object sender, MouseButtonEventArgs args)
    {
        var list = (ItemsControl)sender;

        if (MarqueeSelection.End(list))
        {
            args.Handled = true;
            return;
        }

        // 편집기 안에서 뗀 것이다. 미뤄 둔 클릭은 그대로 두고 (다음 클릭이 접는다) 물러난다.
        if (IsEditing(list, args.OriginalSource as DependencyObject))
        {
            return;
        }

        if (deferred is not { } click || !ReferenceEquals(click.List, sender))
        {
            return;
        }

        deferred = null;

        GetSelectCommand(click.List)?.Execute(click.Item);

        if (IsRenameGesture(Keyboard.Modifiers, args.ClickCount, click.WasSole, click.HadFocus))
        {
            ScheduleRename(click.List);
        }
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        var list = (ItemsControl)sender;

        if (MarqueeSelection.Move(list, args.GetPosition(list), args.LeftButton, Keyboard.Modifiers))
        {
            args.Handled = true;
        }
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs args)
        => MarqueeSelection.Cancel((ItemsControl)sender);

    private static void OnDoubleClick(object sender, MouseButtonEventArgs args)
    {
        var list = (ItemsControl)sender;

        // 편집기 안의 더블클릭은 단어 선택이다. 여기서 열면 이름을 고치던 파일이 실행된다.
        if (IsEditing(list, args.OriginalSource as DependencyObject))
        {
            return;
        }

        // 두 번째 클릭이 왔다 — 이 클릭은 열기다.
        CancelPendingClick();

        // 수정키를 누른 더블클릭은 열기가 아니다 — Ctrl 클릭 두 번은 토글 두 번이다.
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (ItemAt(list, args.OriginalSource as DependencyObject) is { } item)
        {
            GetOpenCommand(list)?.Execute(item);
            args.Handled = true;
        }
    }

    /// <summary>그 페인의 선택. 선택은 행이 아니라 페인이 이름으로 들고 있다 (ADR-011).</summary>
    private static PaneSelection? SelectionOf(ItemsControl list)
        => (list.DataContext as PaneViewModel)?.Selection;

    private static void ScheduleRename(ItemsControl list)
    {
        // UI 스레드에서만 불린다 — Dispatcher 를 여기서 잡는 것이 안전한 이유다.
        renameTimer ??= CreateRenameTimer();
        renameList = list;
        renameTimer.Start();
    }

    private static DispatcherTimer CreateRenameTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };

        timer.Tick += (_, _) =>
        {
            var list = renameList;

            timer.Stop();
            renameList = null;

            if (list is not null)
            {
                GetRenameCommand(list)?.Execute(null);
            }
        };

        return timer;
    }

    private sealed record DeferredClick(ItemsControl List, FileItemViewModel Item, bool WasSole, bool HadFocus);
}
