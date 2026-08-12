using System.Windows;
using System.Windows.Controls;

namespace FlexDir.App.Views;

/// <summary>
/// 분할 격자에서 요소 하나가 맡은 자리 (docs/PRD-v2.md §18).
/// <para>
/// 스플리터가 페인과 같은 열거형에 사는 이유: 배치가 <b>한 표</b>여야 한다. 페인은 여기서
/// 정하고 스플리터는 XAML 트리거로 감추면, 4분할에서 가로 스플리터가 두 열에 걸치는 것
/// 같은 판정이 두 계층으로 갈린다.
/// </para>
/// </summary>
public enum SplitPart
{
    Pane0 = 0,
    Pane1 = 1,
    Pane2 = 2,
    Pane3 = 3,
    ColumnSplitter,
    RowSplitter,
}

/// <summary>격자 안의 한 자리. 값이 그대로 <c>Grid.Row</c>·<c>Grid.Column</c> 이 된다.</summary>
public readonly record struct PanePlacement(int Row, int RowSpan, int Column, int ColumnSpan);

/// <summary>
/// 분할 프리셋을 격자에 펴는 attached behavior (docs/PRD-v2.md §18 · 사용자 결정 2026-08-12).
/// <para>
/// 격자는 <b>열 셋 · 행 셋</b>으로 고정이다 — 왼쪽 <c>*</c> · 스플리터 6 · 오른쪽 <c>*</c>,
/// 그리고 같은 모양의 행. 분할 수가 바뀌면 열·행 정의는 그대로 두고 <b>자식이 어느 칸을
/// 차지하는가</b>만 바뀐다: 정의를 갈아 끼우면 그때마다 페인이 통째로 다시 그려지고, 그것은
/// 스크롤 위치와 선택 표시가 튀는 자리다.
/// </para>
/// <para>
/// <b>판정(<see cref="PlacementFor"/>)만 채점하고 실제로 앉는 것은 사람이 확인한다</b>
/// (CLAUDE.md §5) — <see cref="SplitterSync"/> 와 같은 모양이고 같은 이유다.
/// </para>
/// </summary>
public static class SplitLayout
{
    /// <summary>
    /// 이 격자가 보여 줄 페인 수. <c>WorkspaceViewModel.SplitCount</c> 를 문다.
    /// <para>
    /// <b>기본값이 0 인 것은 의도다.</b> 1 로 두면 1분할로 시작하는 경우에 값이 바뀌지 않아
    /// 아래 콜백이 한 번도 불리지 않는다 — 그러면 자식들이 XAML 에 적힌 자리에 그대로 남는다
    /// (<see cref="SplitterSync"/> 가 <c>Enabled</c> 를 따로 둔 것과 같은 함정이다).
    /// </para>
    /// </summary>
    public static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached(
        "Count",
        typeof(int),
        typeof(SplitLayout),
        new PropertyMetadata(0, OnCountChanged));

    /// <summary>
    /// 이 자식이 맡은 자리. 격자의 자식마다 XAML 에서 한 번 적는다.
    /// </summary>
    public static readonly DependencyProperty PartProperty = DependencyProperty.RegisterAttached(
        "Part",
        typeof(SplitPart),
        typeof(SplitLayout),
        new PropertyMetadata(SplitPart.Pane0));

    public static int GetCount(DependencyObject element) => (int)element.GetValue(CountProperty);

    public static void SetCount(DependencyObject element, int value) => element.SetValue(CountProperty, value);

    public static SplitPart GetPart(DependencyObject element) => (SplitPart)element.GetValue(PartProperty);

    public static void SetPart(DependencyObject element, SplitPart value) => element.SetValue(PartProperty, value);

    /// <summary>
    /// <paramref name="part"/> 가 <paramref name="count"/> 분할에서 앉는 칸.
    /// <see langword="null"/> 이면 그 수에서는 보이지 않는다.
    /// <para>
    /// 표는 <b>자리가 안 옮겨가는 순서</b>다 (사용자에게 보인 배치 2026-08-12): 분할을 늘려도
    /// 이미 있던 페인은 열을 바꾸지 않고 세로로만 줄어든다. 대가는 4분할의 4번이 좌하라는
    /// 것이고 (읽기순서라면 우하다), 얻는 것은 <b>보고 있던 폴더가 화면 반대편으로 날아가지
    /// 않는 것</b>이다.
    /// </para>
    /// <code>
    /// 1분할        2분할        3분할        4분할
    /// ┌───────┐   ┌──┬──┐     ┌──┬──┐     ┌──┬──┐
    /// │       │   │  │  │     │  │ 1│     │ 0│ 1│
    /// │   0   │   │ 0│ 1│     │ 0├──┤     ├──┼──┤
    /// │       │   │  │  │     │  │ 2│     │ 3│ 2│
    /// └───────┘   └──┴──┘     └──┴──┘     └──┴──┘
    /// </code>
    /// </summary>
    public static PanePlacement? PlacementFor(int count, SplitPart part)
    {
        // 바인딩이 아직 서지 않은 순간에는 0 이 온다. 던지지 않는다 — 배치는 화면의 일이고,
        // 화면이 값 하나로 사라지면 원인을 찾기 어렵다 (GlobalViewState.PaneCount 와 같은 판단).
        var panes = count < 1 ? 1 : count > 4 ? 4 : count;

        return part switch
        {
            // 나눌 것이 없을 때만 두 열에 걸친다. 3분할까지는 왼쪽 열 전체를 쓰고,
            // 4분할에서 비로소 위 칸으로 줄어든다.
            SplitPart.Pane0 => panes == 1
                ? new PanePlacement(0, 3, 0, 3)
                : new PanePlacement(0, panes == 4 ? 1 : 3, 0, 1),

            // 오른쪽 열의 위 칸. 2분할에서만 열 전체를 쓴다.
            SplitPart.Pane1 => panes < 2 ? null : new PanePlacement(0, panes == 2 ? 3 : 1, 2, 1),

            SplitPart.Pane2 => panes < 3 ? null : new PanePlacement(2, 1, 2, 1),

            SplitPart.Pane3 => panes < 4 ? null : new PanePlacement(2, 1, 0, 1),

            SplitPart.ColumnSplitter => panes < 2 ? null : new PanePlacement(0, 3, 1, 1),

            // 3분할에서는 오른쪽 열만 나뉜다. 왼쪽까지 걸치면 0번 페인 위에 끌 수 없는
            // 띠가 생긴다. 4분할에서는 두 열을 가로질러 격자의 가로줄이 된다.
            SplitPart.RowSplitter => panes < 3
                ? null
                : panes == 3 ? new PanePlacement(1, 1, 2, 1) : new PanePlacement(1, 1, 0, 3),

            _ => null,
        };
    }

    private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid)
        {
            return;
        }

        if (grid.IsLoaded)
        {
            Apply(grid, (int)e.NewValue);

            return;
        }

        // XAML 파싱 중에는 자식이 아직 없을 수 있다 — 그때 편 것은 아무 데도 닿지 않는다.
        // 정적 메서드라 델리게이트가 같아서 두 번 걸리지 않는다.
        grid.Loaded -= OnGridLoaded;
        grid.Loaded += OnGridLoaded;
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            Apply(grid, GetCount(grid));
        }
    }

    /// <summary>
    /// 격자의 자식들을 제자리에 앉힌다. <b>자식을 넣고 빼지 않는다</b> — 감추는 것은
    /// <see cref="UIElement.Visibility"/> 다: 접힌 페인의 인스턴스가 살아 있어야 다시 폈을 때
    /// 스크롤 위치까지 그대로 돌아온다 (docs/PRD-v2.md §18 · <c>WorkspaceViewModel</c> 이
    /// ViewModel 쪽에서 같은 수를 쓴다).
    /// </summary>
    private static void Apply(Grid grid, int count)
    {
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            if (PlacementFor(count, GetPart(child)) is not { } placement)
            {
                child.Visibility = Visibility.Collapsed;

                continue;
            }

            Grid.SetRow(child, placement.Row);
            Grid.SetRowSpan(child, placement.RowSpan);
            Grid.SetColumn(child, placement.Column);
            Grid.SetColumnSpan(child, placement.ColumnSpan);

            child.Visibility = Visibility.Visible;
        }
    }
}
