using FlexDir.Core.Model;

namespace FlexDir.Core.Enumeration;

/// <summary>
/// 병합 결과. 목록과 <b>이어져야 할 선택</b>을 함께 낸다 — 따로 내면 호출부에서 선택 갱신이
/// 빠지고, 그것이 ADR-011 이 막으려는 실패다.
/// <para>
/// 선택은 이름으로 낸다. 인덱스로 다루면 정렬·삽입마다 어긋난다. 스크롤 위치는 여기 없다 —
/// 픽셀·행 인덱스는 View 의 관심사다.
/// </para>
/// </summary>
public sealed record ReconcileResult(
    IReadOnlyList<FileItem> Items,
    IReadOnlyList<string> Selection);

/// <summary>
/// 외부 변경을 목록에 병합한다. 순수 함수다 — 파일시스템을 읽지 않는다. 다시 읽어온 항목은
/// 호출자가 <c>upserts</c> 로 넘긴다 (그래야 테스트가 결정적이다).
/// <para>
/// 존재 이유는 <b>선택 유지</b>다 — "갱신 때마다 선택이 풀리면 감시가 없느니만 못하다"
/// (ADR-011 · CLAUDE.md §4).
/// </para>
/// <para>
/// <c>ObservableCollection</c> 을 내지 않는다. Core 는 WPF 를 모르고, 목록 반영과 변경 알림은
/// ViewModel 의 일이다.
/// </para>
/// </summary>
public static class ListReconciler
{
    /// <summary>
    /// <paramref name="current"/> 에 변경을 적용한 새 목록과, 유지되어야 할 선택 이름을 낸다.
    /// <para>
    /// 적용 순서는 <b>이름 변경 → 제거 → 갱신</b>이다. 이름 변경을 먼저 처리하지 않으면 옛
    /// 이름이 남아 같은 파일이 두 줄이 된다.
    /// </para>
    /// <para>
    /// 이름 비교는 <see cref="StringComparer.OrdinalIgnoreCase"/> 다 — Windows 파일시스템이
    /// 대소문자를 구분하지 않으므로 갈라지면 같은 파일이 두 줄이 된다.
    /// </para>
    /// </summary>
    /// <param name="comparer">
    /// 결과 목록의 정렬 기준. 삽입 위치를 찾는 대신 전체를 다시 정렬한다 — 정확성이 먼저이고
    /// 성능은 다음 문제다.
    /// </param>
    public static ReconcileResult Apply(
        IReadOnlyList<FileItem> current,
        IReadOnlyCollection<string> selection,
        IReadOnlyList<FileItem> upserts,
        IReadOnlyCollection<string> removals,
        IReadOnlyList<(string OldName, string NewName)> renames,
        IComparer<FileItem> comparer)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(removals);
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(comparer);

        // 이름은 폴더 안에서 유일하다. 그 유일성이 중복을 막는 수단이다.
        var items = new Dictionary<string, FileItem>(current.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in current)
        {
            items[item.Name] = item;
        }

        var selected = new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase);

        // 1. 이름 변경. 사슬(a→b, b→c)이 있으므로 넘어온 순서대로 적용한다.
        foreach (var (oldName, newName) in renames)
        {
            if (items.Remove(oldName, out var renamed))
            {
                // 새 이름의 갱신이 곧 따라오는 것이 보통이지만(ChangeBatch 규칙 5), 그것을
                // 기다리지 않는다 — 갱신이 없어도 목록에 옛 이름이 남아서는 안 된다.
                items[newName] = WithName(renamed, newName);
            }

            // 선택이 새 이름으로 이어진다. 이 함수의 존재 이유가 이 네 줄이다.
            if (selected.Remove(oldName))
            {
                selected.Add(newName);
            }
        }

        // 2. 제거. 사라진 항목은 선택에서도 빠진다.
        foreach (var name in removals)
        {
            items.Remove(name);
            selected.Remove(name);
        }

        // 3. 갱신. 이미 있는 이름은 교체한다 — 중복으로 넣지 않는다.
        foreach (var item in upserts)
        {
            items[item.Name] = item;
        }

        var ordered = new List<FileItem>(items.Values);
        ordered.Sort(comparer);

        // 선택은 결과 목록의 표기와 순서로 낸다. 목록에 없는 이름은 담지 않는다 — 남으면
        // 상태 표시줄 개수가 실제와 어긋난다.
        var carried = new List<string>(selected.Count);
        foreach (var item in ordered)
        {
            if (selected.Contains(item.Name))
            {
                carried.Add(item.Name);
            }
        }

        return new ReconcileResult(ordered, carried);
    }

    /// <summary>
    /// 이름만 바꾼 항목. record 의 <c>with</c> 를 쓰지 않는다 — 복사 생성자는 필드를 그대로
    /// 옮기므로 초기화식이 다시 돌지 않고 <see cref="FileItem.Extension"/> 이 옛 이름의 것으로
    /// 남는다. 유형 컬럼과 아이콘 캐시가 그 값을 키로 쓴다.
    /// </summary>
    private static FileItem WithName(FileItem item, string newName)
    {
        // 위치도 새 이름을 따라간다. 어긋나면 활성화·컨텍스트 메뉴가 없는 파일을 가리킨다.
        // 부모가 없는 것은 드라이브 루트뿐이고, 그것이 폴더 안의 항목으로 오지는 않는다.
        var location = item.Location.TryGetParent(out var folder)
            ? folder.Combine(newName)
            : item.Location;

        return new FileItem(newName, location, item.Size, item.ModifiedUtc, item.Flags);
    }
}
