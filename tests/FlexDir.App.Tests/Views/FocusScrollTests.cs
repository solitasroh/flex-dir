using System.Collections;
using System.Windows;

using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 포커스·편집 대상이 보이도록 목록을 스크롤하는 attached behavior (phase B-4).
/// <para>
/// B-3 이 남긴 결함을 메운다: 이동·선택을 <c>ListView</c> 내장에 맡기지 않으므로
/// (ADR-016) 내장 <c>ScrollIntoView</c> 도 따라오지 않는다 — 방향키·type-ahead 가
/// <c>FocusedName</c> 만 옮기고 화면은 그대로였다.
/// </para>
/// <para>
/// 채점하는 것은 <b>무엇을 보이게 할 것인가</b> 하나다. 소스가 둘이라 (ADR-016) 이
/// 판정이 필요하다 — Details 는 항목이 곧 목록의 원소지만 wrap 뷰 3종에서 원소는
/// <b>행</b>이다. 실제 스크롤은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class FocusScrollTests
{
    // ── 무엇을 보이게 할 것인가 (ADR-016) ─────────────────────────

    [Fact]
    public void Target_InDetails_IsTheItemItself()
    {
        IEnumerable source = new object[] { Item("a.txt"), Item("b.txt") };

        Assert.Equal("b.txt", Assert.IsType<FileItemViewModel>(FocusScroll.Target(source, "b.txt")).Name);
    }

    [Fact]
    public void Target_InAWrapView_IsTheRowNotTheItem()
    {
        // 목록의 원소가 행이므로 (ADR-016) 항목을 주면 ScrollIntoView 가 찾지 못한다.
        var second = new RowViewModel([Item("c.txt"), Item("d.txt")], 2);
        IEnumerable source = new object[] { new RowViewModel([Item("a.txt"), Item("b.txt")], 2), second };

        Assert.Same(second, FocusScroll.Target(source, "d.txt"));
    }

    [Fact]
    public void Target_MatchesLikeTheFileSystem_IgnoringCase()
    {
        IEnumerable source = new object[] { Item("Report.TXT") };

        Assert.NotNull(FocusScroll.Target(source, "report.txt"));
    }

    [Fact]
    public void Target_ForANameThatIsNotThereYet_IsNothing()
    {
        // 새 폴더는 만든 직후 목록에 없다 — 감시가 넣는다 (CLAUDE.md §4).
        IEnumerable source = new object[] { Item("a.txt") };

        Assert.Null(FocusScroll.Target(source, "새 폴더"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Target_WithoutAName_IsNothing(string? name)
    {
        Assert.Null(FocusScroll.Target(new object[] { Item("a.txt") }, name));
    }

    [Fact]
    public void Target_WithoutASource_IsNothing()
    {
        Assert.Null(FocusScroll.Target(null, "a.txt"));
    }

    [Fact]
    public void Target_IgnoresElementsItDoesNotKnow()
    {
        // 바인딩이 아직 붙지 않으면 엉뚱한 것이 들어온다. 예외를 내면 목록 전체가 죽는다.
        Assert.Null(FocusScroll.Target(new object[] { "a.txt", 1 }, "a.txt"));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Names_RoundTrip()
    {
        var element = new DependencyObject();

        FocusScroll.SetFocusedName(element, "a.txt");
        FocusScroll.SetRenamingName(element, "새 폴더");

        Assert.Equal("a.txt", FocusScroll.GetFocusedName(element));
        Assert.Equal("새 폴더", FocusScroll.GetRenamingName(element));
    }

    [Fact]
    public void Names_DefaultToNull()
    {
        var element = new DependencyObject();

        Assert.Null(FocusScroll.GetFocusedName(element));
        Assert.Null(FocusScroll.GetRenamingName(element));
    }

    private static FileItemViewModel Item(string name)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out _));

        return new FileItemViewModel(
            new FileItem(name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None),
            string.Empty,
            string.Empty,
            string.Empty);
    }
}
