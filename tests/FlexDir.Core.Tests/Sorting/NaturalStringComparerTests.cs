using FlexDir.Core.Sorting;

using Xunit;

namespace FlexDir.Core.Tests.Sorting;

/// <summary>
/// 자연 정렬 규칙 (docs/PRD.md §2 — <c>file2 &lt; file10</c>).
/// 여기 고정한 순서가 우리 순서다 — 탐색기(<c>StrCmpLogicalW</c>)와 미세한 차이가
/// 남는 것은 알려진 트레이드오프다 (CLAUDE.md §1: Core 는 Windows API 를 부르지 않는다).
/// </summary>
public class NaturalStringComparerTests
{
    // ── 숫자 런은 수치로 비교한다 ────────────────────────────────────

    [Fact]
    public void DigitRun_ComparesNumerically_NotLexically()
    {
        AssertLess("file2", "file10");
    }

    [Fact]
    public void DigitRun_InTheMiddle_ComparesNumerically()
    {
        AssertLess("a1b", "a10b");
    }

    [Fact]
    public void Sorting_OrdersNumberedNamesByValue()
    {
        var names = new[] { "file10", "file1", "file20", "file3", "file2" };

        Array.Sort(names, NaturalStringComparer.Instance);

        Assert.Equal(new[] { "file1", "file2", "file3", "file10", "file20" }, names);
    }

    // ── long 으로 담을 수 없는 자릿수 ────────────────────────────────
    // long.Parse 에 의존하면 여기서 예외가 난다. 선행 0 제거 → 길이 → 자릿값 순으로 본다.

    [Fact]
    public void TwentyDigitNumbers_CompareWithoutOverflow()
    {
        AssertLess("f99999999999999999999", "f100000000000000000000");
    }

    [Fact]
    public void TwentyDigitNumbers_SameLength_CompareByDigits()
    {
        AssertLess("f12345678901234567890", "f12345678901234567891");
    }

    [Fact]
    public void HugeNumber_WithLeadingZeros_EqualsItsTrimmedForm()
    {
        // 선행 0 만 다르므로 수치는 같다 — 표기 규칙으로만 갈린다.
        AssertLess("f99999999999999999999", "f0099999999999999999999");
    }

    // ── 수치 동률: 선행 0 이 적은 쪽이 먼저 ──────────────────────────
    // 완전 동률로 두면 갱신·뷰 전환마다 순서가 흔들린다.

    [Fact]
    public void SameValue_FewerLeadingZerosFirst()
    {
        AssertLess("img1", "img001");
    }

    [Fact]
    public void AllZeroRuns_ShorterFirst()
    {
        AssertLess("v0", "v00");
    }

    [Fact]
    public void LeadingZeroDifference_YieldsToLaterContent()
    {
        // 표기 차이는 마지막 수단이다. 뒤에 실제로 다른 내용이 있으면 그쪽이 이긴다 —
        // 그렇지 않으면 img001b 와 img1c 가 b·c 를 무시하고 뒤집힌다.
        AssertLess("img001b", "img1c");
    }

    // ── 문자 런은 OrdinalIgnoreCase ─────────────────────────────────

    [Fact]
    public void TextRun_IgnoresCase()
    {
        // Ordinal 이면 'B'(0x42) < 'a'(0x61) 라 Banana 가 앞선다.
        AssertLess("apple", "Banana");
    }

    [Fact]
    public void DigitRun_SortsBeforeTextRun()
    {
        AssertLess("file1", "filea");
    }

    [Fact]
    public void ShorterPrefix_ComesFirst()
    {
        AssertLess("file", "file2");
    }

    [Fact]
    public void CultureSensitiveOrder_IsNotUsed()
    {
        // OrdinalIgnoreCase 는 'a' 를 'A'(0x41) 로 접으므로 '_'(0x5F) 보다 앞이다.
        // 언어별 정렬(CompareInfo)은 구두점을 먼저 놓아 이것을 뒤집는다 —
        // 로케일이 순서를 바꾸면 폴더별로 기억한 정렬 상태가 다른 결과를 낸다.
        AssertLess("atmp", "_tmp");
    }

    // ── 대소문자만 다른 이름을 동률로 두지 않는다 ─────────────────────

    [Fact]
    public void CaseOnlyDifference_IsNotTied()
    {
        Assert.NotEqual(0, NaturalStringComparer.Instance.Compare("readme", "README"));
    }

    [Fact]
    public void CaseOnlyDifference_FallsBackToOrdinal()
    {
        // Ordinal 이므로 대문자가 먼저다. 방향보다 '항상 같은 방향' 인 것이 중요하다.
        AssertLess("README", "readme");
    }

    [Fact]
    public void CaseOnlyDifference_SortsDeterministically()
    {
        var names = new[] { "readme", "README", "ReadMe" };

        Array.Sort(names, NaturalStringComparer.Instance);

        Assert.Equal(new[] { "README", "ReadMe", "readme" }, names);
    }

    // ── 동일·null ───────────────────────────────────────────────────

    [Fact]
    public void IdenticalStrings_AreEqual()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("file10.txt", "file10.txt"));
    }

    [Fact]
    public void Nulls_SortFirstAndAreEqualToEachOther()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare(null, null));
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a", null) > 0);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    /// <summary>순서와 반대 방향을 함께 확인한다. 한쪽만 보면 비대칭 비교기를 놓친다.</summary>
    private static void AssertLess(string smaller, string larger)
    {
        Assert.True(
            NaturalStringComparer.Instance.Compare(smaller, larger) < 0,
            $"'{smaller}' < '{larger}' 를 기대했다");
        Assert.True(
            NaturalStringComparer.Instance.Compare(larger, smaller) > 0,
            $"'{larger}' > '{smaller}' 를 기대했다");
    }
}
