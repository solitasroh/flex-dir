using FlexDir.Core.Model;

namespace FlexDir.Core.Sorting;

/// <summary>
/// 정렬 기준. Details 컬럼과 1:1 이다 (docs/DESIGN.md §3).
/// </summary>
public enum SortKey
{
    Name,
    Size,
    Type,
    Modified,
}

/// <summary>정렬 기준 하나와 방향. 방향 표시는 컬럼 헤더가 한다 (docs/DESIGN.md §6).</summary>
public sealed record SortOrder(SortKey Key, bool Descending = false);

/// <summary>
/// 목록 정렬 (docs/PRD.md §2 — 자연 정렬 · 폴더 먼저 · 다중 키).
/// 비교는 <see cref="FileItem"/> 이 이미 들고 있는 값만 본다. 파일시스템을 읽지 않는다 —
/// 10만 항목 정렬에 I/O 가 섞이면 UI 가 멈춘다.
/// </summary>
public sealed class FileItemComparer : IComparer<FileItem>
{
    private readonly SortOrder[] keys;
    private readonly bool directoriesFirst;

    /// <param name="keys">앞에서부터 적용한다. 비어 있으면 <see cref="ArgumentException"/>.</param>
    /// <param name="directoriesFirst">
    /// 폴더를 항상 위에 둔다. <see cref="SortOrder.Descending"/> 과 무관하다 —
    /// 내림차순마다 폴더가 바닥으로 내려가면 상위 이동이 어려워진다.
    /// </param>
    public FileItemComparer(IReadOnlyList<SortOrder> keys, bool directoriesFirst = true)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            // 조용히 기본값으로 바꾸면 폴더별 뷰상태가 어긋난 것을 놓친다.
            throw new ArgumentException("정렬 기준이 비어 있다.", nameof(keys));
        }

        this.keys = [.. keys];
        this.directoriesFirst = directoriesFirst;
    }

    /// <summary>이름 오름차순, 폴더 먼저.</summary>
    public static FileItemComparer Default { get; } = new([new SortOrder(SortKey.Name)]);

    public int Compare(FileItem? x, FileItem? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        if (directoriesFirst && x.IsDirectory != y.IsDirectory)
        {
            return x.IsDirectory ? -1 : 1;
        }

        foreach (var order in keys)
        {
            var compared = Math.Sign(CompareBy(order.Key, x, y));
            if (compared != 0)
            {
                return order.Descending ? -compared : compared;
            }
        }

        // 모든 키가 동률이면 이름 오름차순. 방향을 뒤집지 않는다 — 갱신·뷰 전환 후
        // 순서가 흔들리면 선택이 엉뚱한 줄로 옮겨간다.
        return CompareNames(x, y);
    }

    private static int CompareBy(SortKey key, FileItem x, FileItem y) => key switch
    {
        SortKey.Name => CompareNames(x, y),
        SortKey.Size => SizeOf(x).CompareTo(SizeOf(y)),

        // 확장자가 같으면 0 을 내고 다음 키(없으면 이름 tie-break)로 넘어간다.
        SortKey.Type => NaturalStringComparer.Instance.Compare(x.Extension, y.Extension),
        SortKey.Modified => x.ModifiedUtc.CompareTo(y.ModifiedUtc),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "알 수 없는 정렬 기준이다."),
    };

    private static int CompareNames(FileItem x, FileItem y)
        => NaturalStringComparer.Instance.Compare(x.Name, y.Name);

    /// <summary>
    /// 디렉터리는 크기를 0 으로 본다. 폴더 용량 계산은 v1 범위 밖이며
    /// (docs/PRD.md §3) 비교 함수 안에서 계산할 수 있는 값도 아니다.
    /// </summary>
    private static long SizeOf(FileItem item) => item.IsDirectory ? 0L : item.Size;
}
