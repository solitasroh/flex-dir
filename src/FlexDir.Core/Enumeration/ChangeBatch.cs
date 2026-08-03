using FlexDir.Core.Watching;

namespace FlexDir.Core.Enumeration;

/// <summary>
/// 감시가 낸 알림 묶음을 "무엇을 다시 읽어야 하는가" 로 번역한 것. 순수 함수의 결과이며
/// I/O 는 하지 않는다 — 다시 읽는 것은 호출자의 일이다.
/// <para>
/// <see cref="IFolderWatcher"/> 가 디바운스·병합을 하지 않는 이유가 이것이다. 얼마나 모아서
/// 처리할지는 소비자의 정책이고, 그 정책은 <see cref="From"/> 에 넘길 묶음을 어떻게 끊느냐로
/// 나타난다.
/// </para>
/// </summary>
/// <param name="NeedsRefresh">다시 읽어야 할 이름 (추가·변경된 것과 이름이 바뀐 새 이름).</param>
/// <param name="Removals">목록에서 빼야 할 이름.</param>
/// <param name="Renames">
/// 적용 순서를 지켜야 한다 — <c>a→b</c>, <c>b→c</c> 사슬은 순서대로 적용해야 맞는다.
/// </param>
/// <param name="RequiresFullRefresh">
/// 오버플로가 하나라도 있었다. 전체를 다시 읽어야 한다.
/// </param>
public sealed record ChangeBatch(
    IReadOnlyList<string> NeedsRefresh,
    IReadOnlyList<string> Removals,
    IReadOnlyList<(string OldName, string NewName)> Renames,
    bool RequiresFullRefresh)
{
    /// <summary>
    /// 알림 묶음을 번역한다. 같은 이름은 한 번으로 합치고, 한 이름의 최종 상태는
    /// <b>마지막 알림</b>이 정한다 (추가 뒤 제거는 제거, 제거 뒤 추가는 갱신).
    /// <para>
    /// 이름 변경의 <c>OldName</c> 은 <see cref="Removals"/> 에 넣지 않는다. 이름 변경은
    /// 제거가 아니며, 제거로 처리하면 선택이 풀린다 (ADR-011).
    /// </para>
    /// </summary>
    public static ChangeBatch From(IReadOnlyList<FolderChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // 오버플로는 나머지를 지운다. 전체를 다시 읽을 것이므로 개별 목록은 낭비이고,
        // 유실된 이벤트 때문에 어차피 불완전하다 — 들고 다니면 '부분 처리로 충분하다' 는
        // 착각이 된다.
        if (changes.Any(change => change.Kind == FolderChangeKind.Overflow))
        {
            return new ChangeBatch([], [], [], RequiresFullRefresh: true);
        }

        // 이름 → 최종 상태(제거인가). 대소문자를 구분하지 않는다 (Windows 파일시스템).
        var removed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 처음 등장 순서. 표기도 처음 본 것을 쓴다 — 어차피 이름 비교는 OrdinalIgnoreCase 다.
        var order = new List<string>();

        var renames = new List<(string OldName, string NewName)>();

        // 같은 이름 변경이 두 번 오면 한 번으로 합친다. '\0' 은 파일 이름에 쓸 수 없으므로
        // 두 이름을 잇는 구분자로 안전하다.
        var seenRenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Mark(string name, bool isRemoval)
        {
            if (!removed.ContainsKey(name))
            {
                order.Add(name);
            }

            removed[name] = isRemoval;
        }

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case FolderChangeKind.Added:
                case FolderChangeKind.Changed:
                    Mark(change.Name, isRemoval: false);
                    break;

                case FolderChangeKind.Removed:
                    Mark(change.Name, isRemoval: true);
                    break;

                case FolderChangeKind.Renamed:
                    // 이름이 바뀌면 확장자·유형·아이콘이 바뀐다. 새 이름은 다시 읽는다.
                    Mark(change.Name, isRemoval: false);

                    if (change.OldName is { Length: > 0 } oldName
                        && seenRenames.Add(oldName + '\0' + change.Name))
                    {
                        renames.Add((oldName, change.Name));
                    }

                    break;

                default:
                    // 오버플로는 위에서 걸렸다.
                    throw new ArgumentOutOfRangeException(
                        nameof(changes), change.Kind, "알 수 없는 변경 종류다.");
            }
        }

        return new ChangeBatch(
            [.. order.Where(name => !removed[name])],
            [.. order.Where(name => removed[name])],
            renames,
            RequiresFullRefresh: false);
    }
}
