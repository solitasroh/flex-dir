using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 배치 추가에 쓰는 관찰 컬렉션. 알림을 <b>배치당 한 번</b>으로 줄인다 —
/// 항목마다 <c>Add</c> 알림을 내면 256개 배치가 256번의 레이아웃 패스가 된다.
/// <para>
/// 아래 두 메서드는 <c>Reset</c> 알림을 낸다. WPF <c>ListView</c> 는 <c>Reset</c> 에서 선택을
/// 버리므로 <b>열거 중에만</b> 쓴다 — 그때는 아직 선택이 없다. 외부 변경을 반영하는 갱신
/// 경로는 개별 <c>Add</c>/<c>Remove</c> 알림을 써야 한다 (CLAUDE.md §4 — 갱신 중에도 선택은
/// 유지한다).
/// </para>
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>여러 항목을 넣고 <c>Reset</c> 알림을 한 번만 낸다. 빈 입력은 알리지 않는다.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var added = false;

        foreach (var item in items)
        {
            // 기반 컬렉션을 직접 건드린다 — Add 를 쓰면 항목마다 알림이 나간다.
            Items.Add(item);
            added = true;
        }

        if (added)
        {
            RaiseReset();
        }
    }

    /// <summary>
    /// 항목을 통째로 교체하고 <c>Reset</c> 알림을 한 번만 낸다.
    /// 빈 입력은 비우는 것이다 — 이전 폴더 항목이 남으면 잘못된 폴더의 내용으로 보인다.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var hadItems = Items.Count > 0;

        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(item);
        }

        // 비어 있던 것을 비운 채로 두었으면 아무것도 바뀌지 않았다.
        if (hadItems || Items.Count > 0)
        {
            RaiseReset();
        }
    }

    private void RaiseReset()
    {
        // Count 알림이 없으면 개수에 걸린 바인딩이 갱신되지 않는다.
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
