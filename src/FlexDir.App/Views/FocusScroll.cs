using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 포커스 항목·편집 대상이 보이도록 목록을 스크롤하는 attached behavior (phase B-4).
/// <para>
/// 이동·선택을 <c>ListView</c> 내장에 맡기지 않으므로 (ADR-016) 내장 스크롤 추적도 없다 —
/// B-3 실물에서 방향키와 type-ahead 가 <c>FocusedName</c> 만 옮기고 화면은 그대로였다.
/// </para>
/// <para>
/// 이름을 <b>바인딩으로</b> 받는다. <c>PropertyChanged</c> 를 날로 받으면 UI 밖 스레드에서
/// 오는 갱신에 스레드 친화성 예외가 난다 — B-2 에서 창 배치 복원이 그 자리를 밟았다
/// (.harness/manual-plan.md §B-2). 바인딩 엔진은 마샬링을 대신한다.
/// </para>
/// <para>
/// 판정(<see cref="Target"/>)만 채점한다. 소스가 둘이라 (ADR-016) 그것이 판정거리다 —
/// Details 는 항목이 곧 목록의 원소지만 wrap 뷰 3종에서 원소는 <b>행</b>이다.
/// </para>
/// </summary>
public static class FocusScroll
{
    public static readonly DependencyProperty FocusedNameProperty = DependencyProperty.RegisterAttached(
        "FocusedName",
        typeof(string),
        typeof(FocusScroll),
        new PropertyMetadata(null, OnNameChanged));

    public static readonly DependencyProperty RenamingNameProperty = DependencyProperty.RegisterAttached(
        "RenamingName",
        typeof(string),
        typeof(FocusScroll),
        new PropertyMetadata(null, OnNameChanged));

    /// <summary>
    /// 아직 목록에 없어 스크롤하지 못한 이름. 새 폴더는 만든 직후 목록에 없고
    /// (감시가 넣는다 — CLAUDE.md §4) 그때는 다시 부를 신호가 없다.
    /// </summary>
    private static readonly DependencyProperty PendingProperty = DependencyProperty.RegisterAttached(
        "Pending",
        typeof(string),
        typeof(FocusScroll),
        new PropertyMetadata(null));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked",
        typeof(bool),
        typeof(FocusScroll),
        new PropertyMetadata(false));

    public static string? GetFocusedName(DependencyObject element) => (string?)element.GetValue(FocusedNameProperty);

    public static void SetFocusedName(DependencyObject element, string? value) => element.SetValue(FocusedNameProperty, value);

    public static string? GetRenamingName(DependencyObject element) => (string?)element.GetValue(RenamingNameProperty);

    public static void SetRenamingName(DependencyObject element, string? value) => element.SetValue(RenamingNameProperty, value);

    /// <summary>
    /// 그 이름을 보이게 하려면 목록의 무엇을 스크롤해야 하는가. 없으면 null 이다.
    /// <para>
    /// wrap 뷰 3종에서 목록의 원소는 행이므로 (ADR-016) 항목을 그대로 주면
    /// <c>ScrollIntoView</c> 가 찾지 못한다. 모르는 원소는 건너뛴다 — 바인딩이 아직 붙지
    /// 않았을 때 예외를 내면 목록 전체가 죽는다.
    /// </para>
    /// </summary>
    internal static object? Target(IEnumerable? source, string? name)
    {
        if (source is null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var element in source)
        {
            switch (element)
            {
                case FileItemViewModel item when Same(item.Name, name):
                    return item;

                case RowViewModel row when row.Items.Any(item => Same(item.Name, name)):
                    return row;
            }
        }

        return null;
    }

    private static bool Same(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(left, right);

    private static void OnNameChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is ListBox list)
        {
            Hook(list);
            Show(list);
        }
    }

    /// <summary>
    /// 목록이 채워지는 것을 기다린다. 이름 둘이 같은 콜백으로 오므로 한 번만 건다 —
    /// 목록 컨트롤은 페인마다 하나이고 바꿔 끼우는 조작이 없어 떼는 경로는 없다.
    /// </summary>
    private static void Hook(ListBox list)
    {
        if ((bool)list.GetValue(HookedProperty))
        {
            return;
        }

        list.SetValue(HookedProperty, true);

        // 못 찾았던 이름이 있을 때만 다시 본다. 갱신마다 스크롤하면 사용자가 손으로 옮겨
        // 놓은 화면을 목록이 제자리로 되돌린다.
        ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) =>
        {
            if (GetPending(list) is not null)
            {
                Show(list);
            }
        };
    }

    /// <summary>
    /// 편집 대상이 이긴다 — 새 폴더는 이름을 칠 수 있어야 하고, 그때 포커스는 아직 옛
    /// 항목에 있다.
    /// </summary>
    private static void Show(ListBox list)
    {
        var name = GetRenamingName(list) ?? GetFocusedName(list);

        if (Target(list.Items, name) is not { } target)
        {
            list.SetValue(PendingProperty, name);
            return;
        }

        list.SetValue(PendingProperty, null);
        list.ScrollIntoView(target);
    }

    private static string? GetPending(DependencyObject element) => (string?)element.GetValue(PendingProperty);
}
