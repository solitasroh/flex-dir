using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace FlexDir.App.Views;

/// <summary>
/// 가상화 패널에서 <c>BringIndexIntoView</c> 를 밖으로 연다.
/// <para>
/// 그 메서드는 <see langword="protected"/> 다 — 화면 밖 항목의 컨테이너를 실현시키는
/// 유일한 공개 경로가 없어서 파생 클래스를 하나 둔다. XAML 의 <c>ItemsPanelTemplate</c> 이
/// <see cref="VirtualizingStackPanel"/> 대신 이것을 쓴다.
/// </para>
/// </summary>
public sealed class RealizingStackPanel : VirtualizingStackPanel
{
    public void Realize(int index) => BringIndexIntoView(index);
}

/// <summary>
/// 트리가 따라간 경로를 화면으로 가져오는 attached behavior (docs/PRD-v2.md §10-3).
///
/// <para>
/// <b>왜 필요한가</b>: <c>TreeView</c> 는 사용자가 클릭한 선택은 화면에 보이게 하지만,
/// <b>프로그램이 정한 선택으로는 스크롤하지 않는다.</b> 트리가 페인을 따라가 경로를 펴도
/// 화면이 그대로면 사용자에게는 "안 되는 것" 으로 보인다.
/// </para>
///
/// <para>
/// <b><see cref="FocusScroll"/> 과 같은 자리다.</b> <c>ARCHITECTURE.md</c> §5 가 목록에 대해
/// 적어 둔 것 — <i>"이동·선택을 내장에 맡기지 않는 대가로 화면이 포커스를 따라오지 않는다"</i> —
/// 가 트리에도 그대로 적용된다.
/// </para>
///
/// <para>
/// <b>노드 하나가 아니라 경로를 받는다.</b> 트리는 가상화돼 있어 (같은 §5) 화면 밖 노드는
/// 컨테이너가 아예 없다. 컨테이너는 <b>부모 컨테이너에서만</b> 얻을 수 있으므로 루트부터
/// 차례로 실현하며 내려가야 한다 — 실물에서 <c>SOOJANG</c> 아래 형제가 90개라 대상이 화면
/// 밖이었고, 단순한 <c>BringIntoView</c> 로는 아무 일도 일어나지 않았다.
/// </para>
///
/// <para>
/// 판정(<see cref="ShouldWalk"/>·<see cref="ShouldExpand"/>)만 채점하고, 컨테이너 실현과
/// 스크롤 체감은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class TreeScroll
{
    /// <summary>
    /// 화면으로 가져올 경로 — 루트부터 고른 노드까지 (<c>FolderTreeViewModel.RevealedPath</c>).
    /// </summary>
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path",
        typeof(IEnumerable),
        typeof(TreeScroll),
        new PropertyMetadata(null, OnPathChanged));

    public static IEnumerable? GetPath(DependencyObject element) => (IEnumerable?)element.GetValue(PathProperty);

    public static void SetPath(DependencyObject element, IEnumerable? value) => element.SetValue(PathProperty, value);

    /// <summary>
    /// 걸어갈 것이 있는가. 빈 경로는 <b>지우는 신호가 아니다</b> — 트리 밖 폴더를 열었거나
    /// 아직 아무 곳도 따라가지 않은 것이고, 그때 화면을 건드리면 보고 있던 자리를 잃는다.
    /// </summary>
    public static bool ShouldWalk(int count) => count > 0;

    /// <summary>
    /// 이 단계를 펼쳐야 하는가. <b>마지막은 펴지 않는다</b> — 그것은 고른 폴더이고,
    /// 펴면 사용자가 접어 둔 하위가 멋대로 열린다.
    /// </summary>
    public static bool ShouldExpand(int step, int count) => step < count - 1;

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree || e.NewValue is not IEnumerable items)
        {
            return;
        }

        var path = items.Cast<object>().ToList();

        if (!ShouldWalk(path.Count))
        {
            return;
        }

        // 레이아웃이 한 번 돈 뒤에 걷는다. 여기는 바인딩 콜백이라 방금 펼쳐진 노드의
        // 컨테이너가 아직 없다 — Loaded 우선순위로 미루면 그때는 있다.
        _ = tree.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            () => Walk(tree, path));
    }

    /// <summary>
    /// 루트부터 차례로 컨테이너를 실현하며 내려가고, 마지막을 화면으로 가져온다.
    /// <para>
    /// <b>중간에서 못 찾으면 멈춘다.</b> 목록이 그 사이에 바뀌었거나(감시 갱신) 숨김 정책이
    /// 그 노드를 걸렀을 수 있다 — 갈 수 있는 데까지 간 것으로 충분하다.
    /// </para>
    /// </summary>
    private static void Walk(TreeView tree, List<object> path)
    {
        ItemsControl parent = tree;

        for (var step = 0; step < path.Count; step++)
        {
            if (Realize(parent, path[step]) is not { } container)
            {
                return;
            }

            if (ShouldExpand(step, path.Count))
            {
                container.IsExpanded = true;

                // 자식 컨테이너는 이 레이아웃이 돌아야 생긴다. 생략하면 다음 단계에서
                // 늘 null 이 되어 한 단계밖에 못 내려간다.
                container.UpdateLayout();

                parent = container;
            }
            else
            {
                container.BringIntoView();
            }
        }
    }

    /// <summary>
    /// 항목의 컨테이너. 없으면 가상화 패널에게 그 인덱스를 실현시켜 달라고 한다.
    /// </summary>
    private static TreeViewItem? Realize(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem ready)
        {
            return ready;
        }

        var index = parent.Items.IndexOf(item);

        if (index < 0)
        {
            return null;
        }

        // 패널은 템플릿이 적용된 뒤에야 있다. 접혀 있던 노드가 여기로 온다.
        parent.ApplyTemplate();
        parent.UpdateLayout();

        if (FindPanel(parent) is { } panel)
        {
            panel.Realize(index);
            parent.UpdateLayout();
        }

        return parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
    }

    /// <summary>
    /// 항목을 담는 패널. <c>ItemsHost</c> 는 템플릿이 붙이는 이름이라 컨트롤 종류마다
    /// 다른 자리에 있으므로 이름으로 찾지 않고 생성기를 통해 얻는다.
    /// </summary>
    private static RealizingStackPanel? FindPanel(ItemsControl parent)
        => parent.ItemContainerGenerator is { } generator
            && generator.ContainerFromIndex(0) is DependencyObject first
                ? System.Windows.Media.VisualTreeHelper.GetParent(first) as RealizingStackPanel
                : null;
}
