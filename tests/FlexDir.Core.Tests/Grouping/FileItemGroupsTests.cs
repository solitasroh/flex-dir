using FlexDir.Core.Grouping;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Sorting;

using Xunit;

namespace FlexDir.Core.Tests.Grouping;

/// <summary>
/// Details 그룹화 (docs/PRD-v2.md §6-1). 그룹 경계는 <b>정렬된 목록의 인접 비교</b>로
/// 잡으므로, 여기서 정하는 라벨은 정렬 순서와 어긋나면 안 된다 — 어긋나면 같은 라벨의
/// 헤더가 목록의 두 자리에 나온다. 그 불변식을 <see cref="Labels_OfASortedList_FormContiguousRuns"/>
/// 가 고정한다.
/// </summary>
public class FileItemGroupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 표준 시간대는 <see cref="FlexDir.Core.Formatting.TimestampFormatter"/> 와 같은 규약으로
    /// 인자다 — 함수 안에서 읽으면 테스트가 실행 기계에 묶인다.
    /// </summary>
    private static string Label(FileItem item, SortKey key)
        => FileItemGroups.LabelOf(item, key, Now, TimeZoneInfo.Utc);

    // ── 폴더는 기준과 무관하게 한 그룹이다 ──────────────────────────
    // FileItemComparer 가 폴더를 항상 위로 올리므로 (directoriesFirst), 폴더를 기준별로
    // 나누면 같은 라벨이 폴더 구간과 파일 구간에 두 번 나온다.

    [Theory]
    [InlineData(SortKey.Name)]
    [InlineData(SortKey.Size)]
    [InlineData(SortKey.Type)]
    [InlineData(SortKey.Modified)]
    public void Directory_IsAlwaysOneGroup(SortKey key)
    {
        Assert.Equal("폴더", Label(Dir("아무거나"), key));
    }

    // ── 이름 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("apple.txt", "A")]
    [InlineData("Apple.txt", "A")]
    [InlineData("zebra.txt", "Z")]
    public void Name_UsesTheUppercasedFirstLetter(string name, string expected)
    {
        Assert.Equal(expected, Label(File(name), SortKey.Name));
    }

    [Theory]
    [InlineData("3장.txt")]
    [InlineData("10.txt")]
    [InlineData("007.bin")]
    public void Name_PutsEveryDigitStartInOneBucket(string name)
    {
        Assert.Equal("0-9", Label(File(name), SortKey.Name));
    }

    [Theory]
    [InlineData("가나다.txt", "ㄱ")]
    [InlineData("까치.txt", "ㄱ")]      // 쌍자음은 기본 자음으로 접는다 — 정렬이 가~까를 붙여 놓는다
    [InlineData("나비.txt", "ㄴ")]
    [InlineData("하늘.txt", "ㅎ")]
    public void Name_UsesTheLeadingJamoForHangul(string name, string expected)
    {
        Assert.Equal(expected, Label(File(name), SortKey.Name));
    }

    // 기호를 "기타" 한 라벨로 묶으면 안 된다. OrdinalIgnoreCase 에서 '(' 는 숫자보다
    // 앞이고 '_' 는 'Z' 보다 뒤라, 한 라벨로 묶는 순간 헤더가 두 자리에 생긴다.
    [Theory]
    [InlineData("_temp.txt", "_")]
    [InlineData("(1).txt", "(")]
    [InlineData("#tag.txt", "#")]
    public void Name_UsesTheSymbolItselfSoRunsStayContiguous(string name, string expected)
    {
        Assert.Equal(expected, Label(File(name), SortKey.Name));
    }

    // ── 크기 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(1, "매우 작음 (0–10 KB)")]
    [InlineData(10 * 1024 - 1, "매우 작음 (0–10 KB)")]
    [InlineData(10 * 1024, "작음 (10–100 KB)")]
    [InlineData(100 * 1024, "보통 (100 KB–1 MB)")]
    [InlineData(1024 * 1024, "큼 (1–16 MB)")]
    [InlineData(16 * 1024 * 1024, "매우 큼 (16–128 MB)")]
    [InlineData(128L * 1024 * 1024, "거대 (128 MB 이상)")]
    public void Size_FallsIntoOneOfTheBuckets(long bytes, string expected)
    {
        Assert.Equal(expected, Label(File("x.bin", size: bytes), SortKey.Size));
    }

    // ── 유형 ────────────────────────────────────────────────────────
    // 확장자를 쓴다. shell 이 주는 유형 이름을 쓰지 않는 이유는 둘이다 —
    // 정렬(SortKey.Type)이 확장자로 비교하므로 다른 값을 쓰면 그룹 경계가 정렬과
    // 어긋나고, 유형 이름은 비동기로 도착해 그룹이 늦게 흔들린다.

    [Theory]
    [InlineData("a.PNG", "PNG")]
    [InlineData("b.png", "PNG")]
    [InlineData("report.tar.gz", "GZ")]
    public void Type_UsesTheUppercasedExtension(string name, string expected)
    {
        Assert.Equal(expected, Label(File(name), SortKey.Type));
    }

    [Theory]
    [InlineData("Makefile")]
    [InlineData(".bashrc")]     // 선행 '.' 은 확장자 구분자가 아니다 (FileItem)
    public void Type_LabelsItemsWithoutAnExtension(string name)
    {
        Assert.Equal("확장자 없음", Label(File(name), SortKey.Type));
    }

    // ── 수정한 날짜 ─────────────────────────────────────────────────
    // 기준은 2026-08-06 (목). 이번 주는 월요일 08-03 부터다.

    [Theory]
    [InlineData("2026-08-06", "오늘")]
    [InlineData("2026-08-07", "오늘")]      // 미래 (시계 어긋남·네트워크) 도 오늘로 본다
    [InlineData("2026-08-05", "어제")]
    [InlineData("2026-08-03", "이번 주")]
    [InlineData("2026-08-02", "지난주")]
    [InlineData("2026-07-27", "지난주")]
    [InlineData("2026-07-26", "지난달")]
    [InlineData("2026-07-01", "지난달")]
    [InlineData("2026-06-30", "올해")]
    [InlineData("2026-01-01", "올해")]
    [InlineData("2025-12-31", "오래 전")]
    public void Modified_FallsIntoOneOfTheBuckets(string modified, string expected)
    {
        var item = File("x.txt", modified: DateTimeOffset.Parse(modified + "T09:00:00+00:00"));

        Assert.Equal(expected, Label(item, SortKey.Modified));
    }

    // ── 불변식: 정렬된 목록에서 같은 라벨은 한 덩이로 붙어 있어야 한다 ──
    // 이것이 깨지면 같은 이름의 헤더가 목록의 두 자리에 나온다. 라벨 규칙을 고칠 때
    // 이 테스트가 먼저 깨져야 한다.

    [Theory]
    [InlineData(SortKey.Name, false)]
    [InlineData(SortKey.Name, true)]
    [InlineData(SortKey.Size, false)]
    [InlineData(SortKey.Size, true)]
    [InlineData(SortKey.Type, false)]
    [InlineData(SortKey.Modified, false)]
    [InlineData(SortKey.Modified, true)]
    public void Labels_OfASortedList_FormContiguousRuns(SortKey group, bool descending)
    {
        var items = new[]
        {
            Dir("작업"), Dir("zip"), Dir("가방"),
            File("_temp.log", size: 5), File("(1).txt", size: 200_000),
            File("3장.png", size: 0), File("10.png", size: 40_000_000),
            File("apple.txt", size: 300, modified: Now.AddDays(-1)),
            File("Avocado.TXT", size: 900_000, modified: Now.AddDays(-40)),
            File("zebra.md", size: 20_000, modified: Now.AddYears(-2)),
            File("가나다.txt", size: 5_000_000, modified: Now.AddDays(-3)),
            File("까치.hwp", size: 130L * 1024 * 1024),
            File("나비.png", size: 15_000), File("Makefile"), File(".bashrc"),
        };

        var keys = FileItemGroups.WithGroupKey([new SortOrder(group, descending)], group);
        var sorted = items.Order(new FileItemComparer(keys)).ToArray();

        var labels = sorted.Select(i => Label(i, group)).ToArray();
        var runs = new List<string>();

        foreach (var label in labels)
        {
            if (runs.Count == 0 || runs[^1] != label)
            {
                runs.Add(label);
            }
        }

        Assert.Equal(runs.Distinct().Count(), runs.Count);
    }

    // ── 그룹 키를 정렬 앞에 붙인다 ──────────────────────────────────

    [Fact]
    public void WithGroupKey_ReturnsTheSortUntouchedWhenGroupingIsOff()
    {
        IReadOnlyList<SortOrder> sort = [new SortOrder(SortKey.Size, Descending: true)];

        Assert.Same(sort, FileItemGroups.WithGroupKey(sort, null));
    }

    [Fact]
    public void WithGroupKey_PutsTheGroupKeyFirstAscendingByDefault()
    {
        IReadOnlyList<SortOrder> sort = [new SortOrder(SortKey.Name)];

        var keys = FileItemGroups.WithGroupKey(sort, SortKey.Type);

        Assert.Equal([new SortOrder(SortKey.Type), new SortOrder(SortKey.Name)], keys);
    }

    // 같은 키로 정렬 중이면 그 방향을 따른다 — 헤더의 방향 글리프와 그룹 순서가
    // 어긋나면 사용자가 정렬이 안 먹었다고 읽는다.
    [Fact]
    public void WithGroupKey_FollowsTheSortDirectionWhenTheKeyIsAlreadySorted()
    {
        IReadOnlyList<SortOrder> sort = [new SortOrder(SortKey.Size, Descending: true)];

        var keys = FileItemGroups.WithGroupKey(sort, SortKey.Size);

        Assert.Equal(SortKey.Size, keys[0].Key);
        Assert.True(keys[0].Descending);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private static FileItem File(string name, long size = 0, DateTimeOffset? modified = null)
        => Make(name, size, modified, FileItemFlags.None);

    private static FileItem Dir(string name)
        => Make(name, 0, null, FileItemFlags.Directory);

    private static FileItem Make(string name, long size, DateTimeOffset? modified, FileItemFlags flags)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var parent, out var error), $"파싱 실패: {error}");
        return new FileItem(name, parent.Combine(name), size, modified ?? Now, flags);
    }
}
