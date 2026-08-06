using System.Windows;
using System.Windows.Controls;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 실현된 컨테이너를 <see cref="PaneViewModel.SetVisibleRange"/> 로 미는 attached behavior
/// (phase B-3 · .harness/manual-plan.md §B).
/// <para>
/// <see cref="ViewportSync"/> 와 같은 자리인 <c>ItemsPanel</c> 에 건다. 가상화 패널의
/// <c>Children</c> 이 곧 실현된 컨테이너이고, 그것이 "무엇이 보이는가" 에 가장 가까운
/// 답이다 — 목록 컨트롤에서 물으면 컨테이너를 다시 찾아 내려가야 한다.
/// </para>
/// <para>
/// 신호는 <c>LayoutUpdated</c> 하나다. 스크롤·뷰 전환·목록 갱신 셋이 전부 레이아웃을
/// 지나므로 셋을 따로 훅할 이유가 없고, 따로 훅하면 컨테이너가 아직 실현되지 않은
/// 시점에 물어보는 자리가 생긴다. 대신 <b>같은 목록이면 밀지 않는다</b> —
/// <see cref="ThumbnailRequestScheduler.SetVisibleRange"/> 는 부를 때마다 진행 중 요청을
/// 전부 끊고 새 세대를 연다.
/// </para>
/// <para>
/// 판정(<see cref="Flatten"/>·<see cref="Unchanged"/>)만 채점하고, 이벤트 훅과 스크롤
/// 체감은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class VisibleRangeSync
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(VisibleRangeSync),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 컨테이너의 <c>DataContext</c> 를 항목 목록으로 편다. 소스가 둘인 것이 의도다
    /// (ADR-016) — Details 는 항목이 그대로 오고, wrap 뷰 3종은 합성 행이 온다.
    /// </summary>
    internal static List<FileItemViewModel> Flatten(IEnumerable<object?> containers)
    {
        var visible = new List<FileItemViewModel>();

        foreach (var container in containers)
        {
            switch (container)
            {
                case FileItemViewModel item:
                    visible.Add(item);
                    break;

                case RowViewModel row:
                    visible.AddRange(row.Items);
                    break;
            }
        }

        return visible;
    }

    /// <summary>
    /// 방금 민 것과 같은가. 인스턴스로 비교한다 — 감시 갱신이 같은 이름의 행을 갈아끼우면
    /// 그림이 없는 새 인스턴스이므로 다시 물어야 한다 (스케줄러도 참조로 본다).
    /// </summary>
    internal static bool Unchanged(IReadOnlyList<FileItemViewModel> pushed, IReadOnlyList<FileItemViewModel> visible)
    {
        if (pushed.Count != visible.Count)
        {
            return false;
        }

        for (var index = 0; index < pushed.Count; index++)
        {
            if (!ReferenceEquals(pushed[index], visible[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel || e.NewValue is not true)
        {
            return;
        }

        // 이 패널이 마지막으로 민 목록. 패널마다 하나이고 (뷰를 바꾸면 패널도 새로 생긴다)
        // 끄는 경로가 없으므로 해제 코드도 없다.
        IReadOnlyList<FileItemViewModel> pushed = [];

        panel.LayoutUpdated += (_, _) =>
        {
            if (panel.DataContext is not PaneViewModel pane)
            {
                return;
            }

            var visible = Flatten(panel.Children.OfType<FrameworkElement>().Select(child => child.DataContext));

            if (Unchanged(pushed, visible))
            {
                return;
            }

            pushed = visible;
            pane.SetVisibleRange(visible);
        };
    }
}
