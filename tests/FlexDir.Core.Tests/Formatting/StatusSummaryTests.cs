using System.Globalization;

using FlexDir.Core.Formatting;

using Xunit;

namespace FlexDir.Core.Tests.Formatting;

/// <summary>
/// 상태표시줄 문자열 (docs/DESIGN.md §1 높이 24 · §4 폰트 11).
/// 폭이 좁으므로 짧게 유지하고 구분자는 가운뎃점 하나로 통일한다.
/// 열거 중·빈 폴더 표현은 docs/UI_GUIDE.md §상태 표현, docs/PRD.md §4.
/// </summary>
public class StatusSummaryTests
{
    private const string Dot = "·";

    // ── 항목 수 ─────────────────────────────────────────────────────

    [Fact]
    public void ForItems_ShowsCount()
    {
        Assert.Equal("항목 232개", StatusSummary.ForItems(232, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForItems_GroupsThousands()
    {
        Assert.Equal("항목 1,204개", StatusSummary.ForItems(1204, CultureInfo.InvariantCulture));
        Assert.Equal("항목 1,204개", StatusSummary.ForItems(1204, new CultureInfo("ko-KR")));
    }

    [Fact]
    public void ForItems_ZeroIsNotTheEmptyMessage()
    {
        // 빈 폴더 표현을 고르는 것은 호출자다. 여기서 몰래 바꾸면 두 문구가 갈라진다.
        Assert.Equal("항목 0개", StatusSummary.ForItems(0, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForItems_HasNoSeparator()
    {
        Assert.DoesNotContain(Dot, StatusSummary.ForItems(232, CultureInfo.InvariantCulture));
    }

    // ── 선택 ────────────────────────────────────────────────────────

    [Fact]
    public void ForSelection_ShowsCountsAndSize()
    {
        Assert.Equal(
            "232개 중 3개 선택 · 1.2 MB",
            StatusSummary.ForSelection(232, 3, 1258291, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForSelection_WithoutSelection_MatchesForItems()
    {
        // 호출자가 분기하지 않아도 되게 한다.
        Assert.Equal(
            StatusSummary.ForItems(232, CultureInfo.InvariantCulture),
            StatusSummary.ForSelection(232, 0, 0, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForSelection_GroupsThousandsInBothCounts()
    {
        Assert.Equal(
            "12,345개 중 1,204개 선택 · 1 KB",
            StatusSummary.ForSelection(12345, 1204, 1024, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForSelection_UsesSizeFormatter()
    {
        var formatted = StatusSummary.ForSelection(10, 2, 1073741824, CultureInfo.InvariantCulture);

        Assert.EndsWith(SizeFormatter.Format(1073741824, CultureInfo.InvariantCulture), formatted);
    }

    [Fact]
    public void ForSelection_EmptyFilesStillShowSize()
    {
        // 0 KB 는 크기를 재지 못한 것이 아니라 빈 파일이다. 칸을 비우지 않는다.
        Assert.Equal(
            "10개 중 2개 선택 · 0 KB",
            StatusSummary.ForSelection(10, 2, 0, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForSelection_UsesASingleSeparator()
    {
        var formatted = StatusSummary.ForSelection(232, 3, 1258291, CultureInfo.InvariantCulture);

        Assert.Equal(1, formatted.Split(Dot).Length - 1);
    }

    // ── 열거 중 ─────────────────────────────────────────────────────
    // 목록을 비우지 않고 점진적으로 채우므로 (docs/UI_GUIDE.md §상태 표현)
    // 지금까지 읽은 개수가 최종 개수가 아님을 알려야 한다.

    [Fact]
    public void ForEnumerating_ShowsProgressCount()
    {
        Assert.Equal("항목 1,204개 읽는 중…", StatusSummary.ForEnumerating(1204, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForEnumerating_ContainsTheCount()
    {
        Assert.Contains("1,204", StatusSummary.ForEnumerating(1204, new CultureInfo("ko-KR")));
    }

    [Fact]
    public void ForEnumerating_DiffersFromFinishedText()
    {
        Assert.NotEqual(
            StatusSummary.ForItems(1204, CultureInfo.InvariantCulture),
            StatusSummary.ForEnumerating(1204, CultureInfo.InvariantCulture));
    }

    // ── 빈 폴더 ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_IsTheEmptyFolderMessage()
    {
        Assert.Equal("빈 폴더", StatusSummary.Empty);
    }

    // ── culture 는 반드시 인자로 온다 ───────────────────────────────

    [Fact]
    public void NullCulture_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StatusSummary.ForItems(1, null!));
        Assert.Throws<ArgumentNullException>(() => StatusSummary.ForSelection(1, 1, 0, null!));
        Assert.Throws<ArgumentNullException>(() => StatusSummary.ForEnumerating(1, null!));
    }
}
