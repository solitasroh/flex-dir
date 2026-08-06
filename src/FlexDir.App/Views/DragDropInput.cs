using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;

namespace FlexDir.App.Views;

/// <summary>
/// 페인 간·페인 밖 드래그앤드롭의 attached behavior (phase B-4 · docs/DESIGN.md §9-1).
/// <para>
/// <b><c>FlexDir.Shell</c> 이 필요 없다.</b> WPF <see cref="System.Windows.DataObject"/> 가
/// <c>CF_HDROP</c> 마샬링을 대신하므로 포트를 늘리지 않고 <c>PaneViewModel.DropAsync</c> 만
/// 부른다. 양방향이다 — 탐색기가 실은 것을 받고 탐색기로 내보낸다.
/// </para>
/// <para>
/// <c>DragDrop.DoDragDrop</c> 은 UI 스레드에서만 시작할 수 있고 자체 메시지 루프를 돈다.
/// 이것은 CLAUDE.md §3 위반이 아니다 — 싣는 것이 경로 문자열뿐이라 저장소에 닿지 않고,
/// 멈춰 있는 것이 아니라 사용자를 기다리는 것이다.
/// </para>
/// <para>
/// 판정 넷(<see cref="Effect"/>·<see cref="TargetFolder"/>·
/// <see cref="HasLeftTheStartingPoint"/>·<see cref="Payload"/>)만 채점한다. 모달 루프는
/// 자동 테스트가 밟을 수 없다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class DragDropInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(DragDropInput),
        new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>버튼을 누른 자리. 임계값을 넘기 전까지는 드래그가 아니다.</summary>
    private static Point origin;

    private static bool dragging;

    /// <summary>
    /// 이름변경 편집기 안에서 누른 것인가. 거기서 끄는 것은 <b>글자 선택</b>이지 파일
    /// 드래그가 아니다 — 막지 않으면 이름을 고치다 마우스를 끌면 파일이 딸려 나간다.
    /// </summary>
    private static bool fromEditor;

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 수정키가 뜻하는 것 (docs/DESIGN.md §9-1).
    /// <para>
    /// <b>기본이 복사인 것은 탐색기와 다르다.</b> 탐색기는 같은 볼륨이면 이동인데, 2분할에서
    /// 드래그는 일상 조작이라 같은 손동작이 대상에 따라 원본을 지우기도 안 지우기도 하면
    /// 사고가 난다. 2분할 계보(Total Commander)의 기본도 복사다.
    /// </para>
    /// </summary>
    internal static DragDropEffects Effect(ModifierKeys modifiers)
    {
        // 바로가기는 v1 범위 밖이다. 모르는 조합으로 무엇이든 하는 것보다 아무 일도 하지
        // 않는 쪽이 안전하다 — 되돌릴 수 없는 것은 이동이다.
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            return DragDropEffects.None;
        }

        return modifiers.HasFlag(ModifierKeys.Shift) ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    internal static bool IsMove(DragDropEffects effect) => effect == DragDropEffects.Move;

    /// <summary>
    /// 놓인 자리가 뜻하는 폴더 (docs/DESIGN.md §9-1). 폴더 위면 그 안, 파일 위나 빈 공간이면
    /// 페인이 보고 있는 폴더다 — 파일 위에 놓는 것은 겨냥이 어긋난 것이지 다른 의도가 아니다.
    /// </summary>
    internal static LocationId? TargetFolder(FileItemViewModel? under, LocationId? current)
        => under is { IsDirectory: true } folder ? folder.Item.Location : current;

    /// <summary>
    /// 임계값을 넘었는가. 시스템 값을 쓴다 — 손떨림이 드래그가 되는 경계는 사용자가 정한다.
    /// </summary>
    internal static bool HasLeftTheStartingPoint(Point from, Point to)
        => Math.Abs(to.X - from.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(to.Y - from.Y) > SystemParameters.MinimumVerticalDragDistance;

    /// <summary>
    /// 무엇을 싣는가 — 선택된 이름들의 경로다.
    /// <para>
    /// <c>DisplayPath</c> 다. 탐색기의 파서는 <c>\\?\</c> 확장 접두사를 모른다
    /// (<c>ShellClipboardBridge</c> 와 같은 규칙이고, 어긴 쪽이 조용히 아무것도 못 받는다).
    /// </para>
    /// </summary>
    internal static string[] Payload(PaneSelection selection, LocationId? folder)
        => folder is null
            ? []
            : [.. selection.SelectedNames.Select(name => folder.Combine(name).DisplayPath)];

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list || args.NewValue is not true)
        {
            return;
        }

        list.AllowDrop = true;

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        list.PreviewMouseLeftButtonDown += OnMouseDown;
        list.PreviewMouseMove += OnMouseMove;
        list.DragOver += OnDragOver;
        list.Drop += OnDrop;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        origin = args.GetPosition((IInputElement)sender);
        fromEditor = ListInput.IsEditing((DependencyObject)sender, args.OriginalSource as DependencyObject);
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        var list = (ItemsControl)sender;

        if (args.LeftButton != MouseButtonState.Pressed
            || dragging
            || fromEditor
            || !HasLeftTheStartingPoint(origin, args.GetPosition(list))
            || list.DataContext is not PaneViewModel pane)
        {
            return;
        }

        var paths = Payload(pane.Selection, pane.CurrentLocation);

        if (paths.Length == 0)
        {
            return;
        }

        // 끌기 시작했다 — 이 클릭은 선택을 접지도 이름을 열지도 않는다. 미뤄 둔 선택을
        // 여기서 확정하면 여러 개를 끌고 있는데 하나만 남는다.
        ListInput.CancelPendingClick();

        var data = new DataObject(DataFormats.FileDrop, paths);

        // DoDragDrop 은 놓을 때까지 돌아오지 않는다. 재진입을 막지 않으면 그 안에서 오는
        // 마우스 이동이 또 하나를 시작한다.
        dragging = true;

        try
        {
            DragDrop.DoDragDrop(list, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            dragging = false;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop)
            ? Effect(args.KeyStates.ToModifiers())
            : DragDropEffects.None;

        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        var list = (ItemsControl)sender;

        if (list.DataContext is not PaneViewModel pane
            || args.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var effect = Effect(args.KeyStates.ToModifiers());

        if (effect == DragDropEffects.None)
        {
            return;
        }

        var under = ListInput.ItemAt(list, args.OriginalSource as DependencyObject);

        if (TargetFolder(under, pane.CurrentLocation) is not { } folder)
        {
            return;
        }

        args.Handled = true;

        // 기다리지 않는다 — 드롭 핸들러가 돌아가야 원본 앱의 DoDragDrop 이 풀린다.
        // 실패 사유는 PaneViewModel 이 상태표시줄에 올린다.
        _ = pane.DropAsync(paths, folder, IsMove(effect));
    }
}

/// <summary>
/// 드래그 중의 키 상태를 <see cref="ModifierKeys"/> 로 옮긴다.
/// <para>
/// <c>Keyboard.Modifiers</c> 를 쓰지 않는다 — 드래그는 자체 메시지 루프를 돌아 WPF 의
/// 키보드 상태가 갱신되지 않는다. 원본이 다른 프로세스일 때는 더 그렇다.
/// </para>
/// </summary>
internal static class DragKeyStates
{
    internal static ModifierKeys ToModifiers(this DragDropKeyStates states)
    {
        var modifiers = ModifierKeys.None;

        if (states.HasFlag(DragDropKeyStates.ShiftKey))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (states.HasFlag(DragDropKeyStates.ControlKey))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (states.HasFlag(DragDropKeyStates.AltKey))
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }
}
