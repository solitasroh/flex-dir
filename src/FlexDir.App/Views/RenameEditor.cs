using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>편집 중에 눌린 키가 뜻하는 것 (docs/DESIGN.md §9-1).</summary>
public enum RenameKey
{
    /// <summary>편집을 계속한다. 다만 목록으로 새지는 않는다.</summary>
    Stay,
    Commit,
    Cancel,
}

/// <summary>
/// 이름변경 인라인 편집기의 attached behavior (phase B-4 · docs/DESIGN.md §9-1).
/// <para>
/// 편집기를 <b>여는</b> 것은 여기가 아니다 — 행 템플릿이 <c>RenamingName</c> 과 자기 이름을
/// 비교해 <c>Visibility</c> 를 정한다. 그래서 <b>아직 목록에 없는 이름</b>(방금 만든 폴더)도
/// 그 항목이 들어오는 순간 저절로 열린다. 여기는 열린 뒤의 일만 한다: 이름을 싣고, 포커스를
/// 주고, 초기 범위를 고르고, 키를 판정한다.
/// </para>
/// <para>
/// <b>포커스 상실은 취소다</b> — 탐색기와 다른 유일한 지점이다. 2분할이라 반대편 페인 클릭이
/// 일상 동작이고, 이름변경은 휴지통이 없는 유일한 조작이다.
/// </para>
/// <para>
/// 판정 넷(<see cref="Choose"/>·<see cref="InitialSelection"/>·<see cref="Subject"/>·
/// <see cref="OwnerContainer"/>)만 채점하고 포커스 이동과 이벤트 훅은 사람이 확인한다
/// (CLAUDE.md §5).
/// </para>
/// <para>
/// <b>목록과 트리가 이 편집기를 함께 쓴다</b> (docs/PRD-v2.md §10-2). Enter·Esc·포커스
/// 상실이 두 곳에서 다르게 동작할 이유가 없어서 따로 만들지 않았다.
/// </para>
/// </summary>
public static class RenameEditor
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(RenameEditor),
        new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.RegisterAttached(
        "CommitCommand",
        typeof(ICommand),
        typeof(RenameEditor),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.RegisterAttached(
        "CancelCommand",
        typeof(ICommand),
        typeof(RenameEditor),
        new PropertyMetadata(null));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static ICommand? GetCommitCommand(DependencyObject element) => (ICommand?)element.GetValue(CommitCommandProperty);

    public static void SetCommitCommand(DependencyObject element, ICommand? value) => element.SetValue(CommitCommandProperty, value);

    public static ICommand? GetCancelCommand(DependencyObject element) => (ICommand?)element.GetValue(CancelCommandProperty);

    public static void SetCancelCommand(DependencyObject element, ICommand? value) => element.SetValue(CancelCommandProperty, value);

    /// <summary>
    /// 나머지가 전부 <see cref="RenameKey.Stay"/> 인 것이 규칙이다 — 편집 중에는 방향키도
    /// 단축키도 목록으로 새지 않는다.
    /// </summary>
    internal static RenameKey Choose(Key key) => key switch
    {
        Key.Return => RenameKey.Commit,
        Key.Escape => RenameKey.Cancel,
        _ => RenameKey.Stay,
    };

    /// <summary>
    /// 편집 중에 목록·창으로 새면 안 되는 키인가 (docs/DESIGN.md §9-1).
    /// <para>
    /// <b>글자를 만드는 키는 여기 없다.</b> <c>KeyDown</c> 을 <c>Handled</c> 로 표시하면
    /// WPF 가 그 키에서 <c>TextInput</c> 을 만들지 않아 <b>타이핑이 통째로 죽는다</b> —
    /// 캐럿은 움직이는데 글자만 안 들어간다. B-4 실물에서 이 자리를 밟았다.
    /// </para>
    /// <para>
    /// <c>Delete</c> 를 삼키는 이유는 다르다: 창이 그 키에 휴지통을 걸어 두었으므로
    /// (docs/DESIGN.md §9) 새면 <b>고르던 파일이 사라진다</b>. 지우는 것은 글자여야 한다.
    /// </para>
    /// </summary>
    internal static bool Swallows(Key key, ModifierKeys modifiers)
    {
        // Space 는 글자이면서 Ctrl 조합은 선택 토글이다. 조합일 때만 삼킨다.
        if (key == Key.Space)
        {
            return modifiers.HasFlag(ModifierKeys.Control);
        }

        return key
            is Key.Return or Key.Escape
            or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Tab or Key.Delete or Key.Back
            or Key.F2 or Key.F5;
    }

    /// <summary>
    /// 편집기를 열 때 선택해 둘 범위. 파일은 확장자를 빼고 폴더는 전체다 (탐색기와 같다).
    /// <para>
    /// 확장자는 <b>마지막</b> 점부터다 — <c>a.tar.gz</c> 에서 <c>a.tar</c> 가 선택된다.
    /// 앞점 이름(<c>.gitignore</c>)에는 확장자가 없다: 그 점은 이름의 일부다.
    /// </para>
    /// </summary>
    internal static (int Start, int Length) InitialSelection(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return (0, name.Length);
        }

        var dot = name.LastIndexOf('.');

        return dot <= 0 ? (0, name.Length) : (0, dot);
    }

    /// <summary>
    /// 편집기가 무엇의 이름을 고치고 있나 — 실을 글자와, 초기 선택을 정할 폴더 여부다.
    /// 편집기가 붙을 수 없는 것이면 <c>null</c>.
    /// <para>
    /// 목록 항목과 트리 즐겨찾기 둘이 이 편집기를 함께 쓴다 (docs/PRD-v2.md §10-2).
    /// 트리 쪽은 <b>이미 담아 둔 편집용 글자</b>를 싣는다 — 노드의 표시 이름은 확정할
    /// 때까지 건드리지 않기 때문이다. 그리고 즐겨찾기는 늘 폴더라 전체가 선택된다.
    /// </para>
    /// </summary>
    internal static (string Text, bool IsDirectory)? Subject(object? dataContext) => dataContext switch
    {
        FileItemViewModel item => (item.Name, item.IsDirectory),
        TreeNodeViewModel node => (node.EditingLabel, true),
        _ => null,
    };

    /// <summary>
    /// 편집기가 들어 있는 목록·트리. 편집이 닫히면 키보드를 여기로 돌려준다 — 그러지
    /// 않으면 다음 방향키가 갈 곳이 없다.
    /// <para>
    /// 가장 가까운 <c>ItemsControl</c> 에서 멈추지 않는다: wrap 뷰 3종에서 편집기는 합성
    /// 행 안의 중첩 <c>ItemsControl</c> 에 있고 (ADR-016) 그것은 <c>Focusable=False</c> 다.
    /// 트리도 마찬가지로 <c>TreeViewItem</c> 이 중간에 여럿 끼어 있다.
    /// </para>
    /// </summary>
    internal static Control? OwnerContainer(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ListBox or TreeView)
            {
                return (Control)node;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return null;
    }

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBox box || args.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        box.IsVisibleChanged += OnVisibleChanged;
        box.KeyDown += OnKeyDown;
        box.LostKeyboardFocus += OnLostFocus;
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        var box = (TextBox)sender;

        if (args.NewValue is not true || Subject(box.DataContext) is not { } subject)
        {
            return;
        }

        // 이름을 바인딩으로 싣지 않는다 — 취소했다 다시 열면 지난 입력이 남는다.
        box.Text = subject.Text;

        // 방금 Visible 이 된 요소는 아직 레이아웃을 지나지 않아 포커스를 받지 못한다.
        // 가상화가 컨테이너를 실현하는 중일 수도 있다.
        box.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (!box.IsVisible)
                {
                    return;
                }

                var (start, length) = InitialSelection(box.Text, subject.IsDirectory);

                box.Focus();
                box.Select(start, length);
            });
    }

    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        var box = (TextBox)sender;

        switch (Choose(args.Key))
        {
            case RenameKey.Commit:
                GetCommitCommand(box)?.Execute(box.Text);
                OwnerContainer(box)?.Focus();
                break;

            case RenameKey.Cancel:
                GetCancelCommand(box)?.Execute(null);
                OwnerContainer(box)?.Focus();
                break;
        }

        // 무엇이든 삼키면 안 된다 — 글자를 만드는 키까지 막으면 타이핑이 죽는다
        // (<see cref="Swallows"/>). 여기까지 온 키는 TextBox 가 쓰지 않은 것이므로,
        // 삼킬 것만 삼키고 나머지는 흘려보낸다.
        if (Swallows(args.Key, Keyboard.Modifiers))
        {
            args.Handled = true;
        }
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        // 포커스 상실이 취소다 (docs/DESIGN.md §9-1). 확정·취소로 닫히는 경로도 여기를
        // 지나지만 그때는 편집이 이미 끝나 아무 일도 아니다 — CancelRename 은 멱등이다.
        GetCancelCommand((TextBox)sender)?.Execute(null);
    }
}
