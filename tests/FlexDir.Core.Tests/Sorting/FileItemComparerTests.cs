using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Sorting;

using Xunit;

namespace FlexDir.Core.Tests.Sorting;

/// <summary>
/// 목록 정렬 규칙 (docs/PRD.md §2 — 자연 정렬 · 폴더 먼저 · 다중 키).
/// 정렬 방향 표시는 docs/DESIGN.md §6, 정렬 가능한 컬럼은 §3.
/// </summary>
public class FileItemComparerTests
{
    private static readonly DateTimeOffset Base = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    // ── 폴더 먼저. 방향과 무관하다 ───────────────────────────────────
    // 내림차순마다 폴더가 바닥으로 내려가면 상위 이동이 어려워진다.

    [Fact]
    public void Ascending_PutsDirectoriesFirst()
    {
        var sorted = Sort([File("a.txt"), Dir("zz")], new SortOrder(SortKey.Name));

        Assert.Equal(new[] { "zz", "a.txt" }, sorted);
    }

    [Fact]
    public void Descending_StillPutsDirectoriesFirst()
    {
        var sorted = Sort([File("zz.txt"), Dir("aa")], new SortOrder(SortKey.Name, Descending: true));

        Assert.Equal(new[] { "aa", "zz.txt" }, sorted);
    }

    [Fact]
    public void Descending_ReversesWithinEachGroup()
    {
        var items = new[] { Dir("a"), Dir("b"), File("x.txt"), File("y.txt") };

        var sorted = Sort(items, new SortOrder(SortKey.Name, Descending: true));

        Assert.Equal(new[] { "b", "a", "y.txt", "x.txt" }, sorted);
    }

    [Fact]
    public void SizeDescending_StillPutsDirectoriesFirst()
    {
        var items = new[] { File("big.bin", size: 9_000), Dir("folder"), File("small.bin", size: 1) };

        var sorted = Sort(items, new SortOrder(SortKey.Size, Descending: true));

        Assert.Equal(new[] { "folder", "big.bin", "small.bin" }, sorted);
    }

    [Fact]
    public void DirectoriesFirstDisabled_MixesDirectoriesIntoTheOrder()
    {
        var items = new[] { Dir("m"), File("a.txt"), File("z.txt") };

        var comparer = new FileItemComparer([new SortOrder(SortKey.Name)], directoriesFirst: false);

        Assert.Equal(new[] { "a.txt", "m", "z.txt" }, Sort(items, comparer));
    }

    // ── 이름은 자연 정렬 ────────────────────────────────────────────

    [Fact]
    public void NameKey_UsesNaturalOrder()
    {
        var items = new[] { File("file10.txt"), File("file2.txt"), File("file1.txt") };

        var sorted = Sort(items, new SortOrder(SortKey.Name));

        Assert.Equal(new[] { "file1.txt", "file2.txt", "file10.txt" }, sorted);
    }

    // ── 다중 키 ─────────────────────────────────────────────────────

    [Fact]
    public void MultipleKeys_FallThroughToTheNextKey()
    {
        var items = new[]
        {
            File("b.txt", size: 100),
            File("a.txt", size: 100),
            File("c.txt", size: 500),
        };

        var sorted = Sort(items, new SortOrder(SortKey.Size, Descending: true), new SortOrder(SortKey.Name));

        // 크기 내림차순이 먼저, 크기가 같으면 이름 오름차순.
        Assert.Equal(new[] { "c.txt", "a.txt", "b.txt" }, sorted);
    }

    [Fact]
    public void SecondKeyDirection_IsIndependentOfTheFirst()
    {
        var items = new[] { File("a.txt", size: 100), File("b.txt", size: 100) };

        var sorted = Sort(items, new SortOrder(SortKey.Size), new SortOrder(SortKey.Name, Descending: true));

        Assert.Equal(new[] { "b.txt", "a.txt" }, sorted);
    }

    // ── 모든 키가 동률이면 이름 오름차순 ─────────────────────────────
    // 갱신·뷰 전환 후 순서가 흔들리면 선택이 엉뚱한 줄로 옮겨간다.

    [Fact]
    public void AllKeysTied_FallBackToNameAscending()
    {
        var items = new[] { File("z.txt", size: 10), File("a.txt", size: 10) };

        var sorted = Sort(items, new SortOrder(SortKey.Size));

        Assert.Equal(new[] { "a.txt", "z.txt" }, sorted);
    }

    [Fact]
    public void AllKeysTied_NameTieBreakStaysAscending_EvenWhenKeyIsDescending()
    {
        var items = new[] { File("z.txt", size: 10), File("a.txt", size: 10) };

        var sorted = Sort(items, new SortOrder(SortKey.Size, Descending: true));

        Assert.Equal(new[] { "a.txt", "z.txt" }, sorted);
    }

    // ── 크기: 디렉터리는 0 ──────────────────────────────────────────
    // 폴더 용량은 계산하지 않는다 (docs/PRD.md §3). 파일시스템을 읽으면 비교가 O(1) 이 아니다.

    [Fact]
    public void SizeKey_TreatsDirectoryAsZero()
    {
        var items = new[] { File("tiny.bin", size: 1), Dir("folder", size: 999_999) };

        var comparer = new FileItemComparer([new SortOrder(SortKey.Size)], directoriesFirst: false);

        Assert.Equal(new[] { "folder", "tiny.bin" }, Sort(items, comparer));
    }

    [Fact]
    public void SizeKey_TiesDirectoriesRegardlessOfTheirSizeField()
    {
        var items = new[] { Dir("z", size: 1), Dir("a", size: 900) };

        // 둘 다 0 이므로 이름 tie-break 로 넘어간다.
        Assert.Equal(new[] { "a", "z" }, Sort(items, new SortOrder(SortKey.Size)));
    }

    [Fact]
    public void SizeKey_ComparesFilesByBytes()
    {
        var items = new[] { File("b.bin", size: 2), File("a.bin", size: 30) };

        Assert.Equal(new[] { "b.bin", "a.bin" }, Sort(items, new SortOrder(SortKey.Size)));
    }

    // ── 유형: 확장자 → 이름 ─────────────────────────────────────────

    [Fact]
    public void TypeKey_ComparesExtensions()
    {
        var items = new[] { File("a.txt"), File("z.doc") };

        Assert.Equal(new[] { "z.doc", "a.txt" }, Sort(items, new SortOrder(SortKey.Type)));
    }

    [Fact]
    public void TypeKey_TiedExtension_FallsThroughToName()
    {
        var items = new[] { File("z.txt"), File("a.txt"), File("m.txt") };

        Assert.Equal(new[] { "a.txt", "m.txt", "z.txt" }, Sort(items, new SortOrder(SortKey.Type)));
    }

    [Fact]
    public void TypeKey_UsesNaturalOrderOnExtensions()
    {
        var items = new[] { File("a.part10"), File("b.part2") };

        Assert.Equal(new[] { "b.part2", "a.part10" }, Sort(items, new SortOrder(SortKey.Type)));
    }

    [Fact]
    public void TypeKey_TreatsExtensionCaseInsensitively()
    {
        // FileItem 이 확장자를 소문자로 정규화하므로 .TXT 와 .txt 가 갈라지면 안 된다.
        var items = new[] { File("z.TXT"), File("a.txt") };

        Assert.Equal(new[] { "a.txt", "z.TXT" }, Sort(items, new SortOrder(SortKey.Type)));
    }

    // ── 수정시각 ────────────────────────────────────────────────────

    [Fact]
    public void ModifiedKey_ComparesTimestamps()
    {
        var items = new[]
        {
            File("new.txt", modified: Base.AddHours(1)),
            File("old.txt", modified: Base.AddHours(-1)),
        };

        Assert.Equal(new[] { "old.txt", "new.txt" }, Sort(items, new SortOrder(SortKey.Modified)));
        Assert.Equal(
            new[] { "new.txt", "old.txt" },
            Sort(items, new SortOrder(SortKey.Modified, Descending: true)));
    }

    // ── 기본 비교기 ─────────────────────────────────────────────────

    [Fact]
    public void Default_IsNameAscendingWithDirectoriesFirst()
    {
        var items = new[] { File("file10.txt"), File("file2.txt"), Dir("sub") };

        var sorted = items.Order(FileItemComparer.Default).Select(i => i.Name).ToArray();

        Assert.Equal(new[] { "sub", "file2.txt", "file10.txt" }, sorted);
    }

    // ── 빈 정렬은 호출자의 버그다 ────────────────────────────────────
    // 조용히 기본값으로 바꾸면 폴더별 뷰상태가 어긋난 것을 놓친다.

    [Fact]
    public void EmptyKeys_Throw()
    {
        Assert.Throws<ArgumentException>(() => new FileItemComparer([]));
    }

    // ── null ────────────────────────────────────────────────────────

    [Fact]
    public void Nulls_SortFirstAndAreEqualToEachOther()
    {
        var comparer = FileItemComparer.Default;
        var item = File("a.txt");

        Assert.Equal(0, comparer.Compare(null, null));
        Assert.True(comparer.Compare(null, item) < 0);
        Assert.True(comparer.Compare(item, null) > 0);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private static string[] Sort(IEnumerable<FileItem> items, params SortOrder[] keys)
        => Sort(items, new FileItemComparer(keys));

    private static string[] Sort(IEnumerable<FileItem> items, FileItemComparer comparer)
        => items.Order(comparer).Select(i => i.Name).ToArray();

    private static FileItem File(string name, long size = 0, DateTimeOffset? modified = null)
        => Make(name, size, modified, FileItemFlags.None);

    private static FileItem Dir(string name, long size = 0, DateTimeOffset? modified = null)
        => Make(name, size, modified, FileItemFlags.Directory);

    private static FileItem Make(string name, long size, DateTimeOffset? modified, FileItemFlags flags)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var parent, out var error), $"파싱 실패: {error}");
        return new FileItem(name, parent.Combine(name), size, modified ?? Base, flags);
    }
}
