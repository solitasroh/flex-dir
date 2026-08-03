using CommunityToolkit.Mvvm.ComponentModel;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 페인 하나의 선택. <b>이름으로</b> 보관한다.
/// <para>
/// 인덱스로 다루면 정렬이 바뀔 때 다른 항목을 가리키고, <see cref="FileItemViewModel"/> 참조로
/// 다루면 갱신이 인스턴스를 교체할 때 끊긴다. <c>ListReconciler</c> 가 이어질 선택을 이름
/// 목록으로 내는 이유도 같다 (CLAUDE.md §4 · docs/UI_GUIDE.md §원칙 4).
/// </para>
/// <para>
/// WPF 타입을 쓰지 않는다 — <c>ListView.SelectedItems</c> 와의 동기화는 View 의 일이고,
/// 이 객체는 테스트 가능한 평범한 상태여야 한다.
/// </para>
/// </summary>
public sealed partial class PaneSelection : ObservableObject
{
    /// <summary>파일시스템이 대소문자를 구분하지 않으므로 비교도 구분하지 않는다.</summary>
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);

    private string? anchor;

    /// <summary>선택된 항목 이름. 순서는 보장하지 않는다 — 화면 순서는 목록이 안다.</summary>
    public IReadOnlyCollection<string> SelectedNames => selected;

    public int Count => selected.Count;

    /// <summary>범위 선택(Shift)의 기준점. 선택이 비면 null.</summary>
    public string? Anchor
    {
        get => anchor;
        private set => SetProperty(ref anchor, value);
    }

    /// <summary>하나만 선택한다. 앵커도 이 항목이 된다.</summary>
    public void SelectSingle(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var changed = selected.Count != 1 || !selected.Contains(name);

        selected.Clear();
        selected.Add(name);
        Anchor = name;

        if (changed)
        {
            NotifySelectionChanged();
        }
    }

    /// <summary>토글한다(Ctrl). 앵커는 토글한 항목이 된다.</summary>
    public void Toggle(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!selected.Remove(name))
        {
            selected.Add(name);
        }

        // 선택을 푼 항목이라도 다음 Shift 범위는 여기서 시작한다 — 방금 클릭한 자리다.
        Anchor = selected.Count == 0 ? null : name;

        NotifySelectionChanged();
    }

    /// <summary>
    /// 앵커부터 지정 항목까지 선택한다(Shift). 앵커가 없으면 <see cref="SelectSingle"/> 과 같다.
    /// </summary>
    /// <param name="order">
    /// 현재 화면 순서. 정렬이 바뀌면 같은 두 항목이 다른 범위가 된다 — 그래서 인자로 받는다.
    /// </param>
    public void SelectRange(string name, IReadOnlyList<string> order)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(order);

        var from = anchor is null ? -1 : IndexOf(order, anchor);
        var to = IndexOf(order, name);

        // 갱신과 조작이 겹치면 목록에 없는 이름이 온다. 정상 상황이므로 예외를 던지지 않고
        // 단일 선택으로 물러난다 — 클릭이 아무 일도 하지 않는 것보다 낫다.
        if (from < 0 || to < 0)
        {
            SelectSingle(name);
            return;
        }

        var (start, end) = from <= to ? (from, to) : (to, from);

        // 기존 선택은 대체된다 (탐색기와 동일).
        selected.Clear();

        for (var index = start; index <= end; index++)
        {
            selected.Add(order[index]);
        }

        // 앵커는 그대로 둔다 — Shift 를 누른 채 방향을 바꿀 수 있어야 한다.
        NotifySelectionChanged();
    }

    public void Clear()
    {
        Anchor = null;

        if (selected.Count == 0)
        {
            return;
        }

        selected.Clear();
        NotifySelectionChanged();
    }

    /// <summary>
    /// 목록이 바뀐 뒤 존재하지 않는 이름을 떨군다. 남은 선택은 유지한다
    /// (CLAUDE.md §4 — 갱신 중에도 선택은 유지한다).
    /// </summary>
    public void Retain(IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        // 없어진 것만 본다. 남은 이름의 표기도 그대로 둔다 — 목록과 대소문자만 다를 수 있다.
        var dropped = selected.RemoveWhere(name => !existing.Contains(name));

        if (selected.Count == 0 || (anchor is not null && !existing.Contains(anchor)))
        {
            Anchor = null;
        }

        if (dropped > 0)
        {
            NotifySelectionChanged();
        }
    }

    /// <summary>reconcile 결과를 그대로 반영한다 (이름 변경으로 이어진 선택 포함).</summary>
    public void ReplaceWith(IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var replacement = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var changed = !replacement.SetEquals(selected);

        selected.Clear();

        foreach (var name in replacement)
        {
            selected.Add(name);
        }

        // 앵커가 결과에 없으면 기준점이 사라진 것이다 (지워졌거나 이름이 바뀌었다).
        if (anchor is null || !selected.Contains(anchor))
        {
            Anchor = null;
        }

        if (changed)
        {
            NotifySelectionChanged();
        }
    }

    public bool IsSelected(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return selected.Contains(name);
    }

    private static int IndexOf(IReadOnlyList<string> order, string name)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(order[index], name))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>상태표시줄이 이 둘을 본다. 집합을 그대로 노출하므로 개수와 함께 알린다.</summary>
    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNames));
        OnPropertyChanged(nameof(Count));
    }
}
