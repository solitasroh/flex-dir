using FlexDir.App.ViewModels;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 선택은 <b>이름으로</b> 보관한다 (CLAUDE.md §4 · docs/UI_GUIDE.md §원칙 4).
/// 인덱스로 다루면 정렬이 바뀔 때 다른 항목을 가리키고, 인스턴스로 다루면 갱신이 참조를 끊는다.
/// <c>ListReconciler</c> 가 이어질 선택을 이름 목록으로 내는 이유도 같다.
/// </summary>
public class PaneSelectionTests
{
    private readonly PaneSelection selection = new();

    // ── 단일 선택 ─────────────────────────────────────────────────

    [Fact]
    public void New_HasNothingSelected()
    {
        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.SelectedNames);
        Assert.Null(selection.Anchor);
        Assert.False(selection.IsSelected("a.txt"));
    }

    [Fact]
    public void SelectSingle_SelectsOneItemAndBecomesTheAnchor()
    {
        selection.SelectSingle("b.txt");

        Assert.Equal(1, selection.Count);
        Assert.Equal(["b.txt"], Names());
        Assert.Equal("b.txt", selection.Anchor);
        Assert.True(selection.IsSelected("b.txt"));
    }

    [Fact]
    public void SelectSingle_ReplacesThePreviousSelection()
    {
        selection.SelectSingle("a.txt");
        selection.Toggle("b.txt");

        selection.SelectSingle("c.txt");

        Assert.Equal(["c.txt"], Names());
        Assert.Equal("c.txt", selection.Anchor);
    }

    // ── 토글 (Ctrl) ───────────────────────────────────────────────

    [Fact]
    public void Toggle_AddsToTheSelectionAndMovesTheAnchor()
    {
        selection.SelectSingle("a.txt");

        selection.Toggle("c.txt");

        Assert.Equal(["a.txt", "c.txt"], Names());
        Assert.Equal("c.txt", selection.Anchor);
    }

    [Fact]
    public void Toggle_Twice_LeavesNothingSelected()
    {
        selection.Toggle("a.txt");
        selection.Toggle("a.txt");

        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.SelectedNames);

        // 선택이 비면 범위의 기준점도 없다.
        Assert.Null(selection.Anchor);
    }

    [Fact]
    public void Toggle_IgnoresCase()
    {
        // Windows 파일시스템은 대소문자를 구분하지 않는다. 갈라지면 같은 파일이 두 번 선택된다.
        selection.Toggle("Report.TXT");
        selection.Toggle("report.txt");

        Assert.Equal(0, selection.Count);
    }

    // ── 범위 선택 (Shift) ─────────────────────────────────────────

    [Fact]
    public void SelectRange_SelectsEverythingBetweenTheAnchorAndTheTarget()
    {
        selection.SelectSingle("A");

        selection.SelectRange("D", Order);

        Assert.Equal(4, selection.Count);
        Assert.Equal(["A", "B", "C", "D"], Names());
        Assert.Equal("A", selection.Anchor);
    }

    [Fact]
    public void SelectRange_Backwards_SelectsTheSameRange()
    {
        selection.SelectSingle("D");

        selection.SelectRange("B", Order);

        Assert.Equal(["B", "C", "D"], Names());
        Assert.Equal("D", selection.Anchor);
    }

    [Fact]
    public void SelectRange_ChangingDirection_KeepsTheAnchor()
    {
        // Shift 를 누른 채 방향을 바꿀 수 있어야 한다 — 앵커가 따라 움직이면 범위가 자란다.
        selection.SelectSingle("C");
        selection.SelectRange("E", Order);

        selection.SelectRange("A", Order);

        Assert.Equal(["A", "B", "C"], Names());
        Assert.Equal("C", selection.Anchor);
    }

    [Fact]
    public void SelectRange_ReplacesThePreviousSelection()
    {
        // 탐색기와 같다 — Shift 범위는 이전 선택을 남기지 않는다.
        selection.SelectSingle("A");
        selection.Toggle("E");
        selection.SelectSingle("B");

        selection.SelectRange("C", Order);

        Assert.Equal(["B", "C"], Names());
    }

    [Fact]
    public void SelectRange_WithoutAnAnchor_FallsBackToASingleSelection()
    {
        selection.SelectRange("C", Order);

        Assert.Equal(["C"], Names());
        Assert.Equal("C", selection.Anchor);
    }

    [Fact]
    public void SelectRange_TargetMissingFromTheOrder_FallsBackWithoutThrowing()
    {
        // 갱신과 클릭이 겹치는 것은 정상 상황이다. 예외로 만들면 호출부마다 잡아야 한다.
        selection.SelectSingle("A");

        selection.SelectRange("gone.txt", Order);

        Assert.Equal(["gone.txt"], Names());
        Assert.Equal("gone.txt", selection.Anchor);
    }

    [Fact]
    public void SelectRange_AnchorMissingFromTheOrder_FallsBackWithoutThrowing()
    {
        selection.SelectSingle("gone.txt");

        selection.SelectRange("C", Order);

        Assert.Equal(["C"], Names());
        Assert.Equal("C", selection.Anchor);
    }

    [Fact]
    public void SelectRange_IgnoresCase()
    {
        selection.SelectSingle("a");

        selection.SelectRange("c", ["A", "B", "C", "D"]);

        Assert.Equal(["A", "B", "C"], Names());
    }

    // ── 비우기 ────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesTheSelectionAndDropsTheAnchor()
    {
        selection.SelectSingle("A");
        selection.SelectRange("C", Order);

        selection.Clear();

        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.SelectedNames);
        Assert.Null(selection.Anchor);
    }

    // ── 목록이 바뀐 뒤 (Retain) ───────────────────────────────────

    [Fact]
    public void Retain_DropsOnlyTheNamesThatDisappeared()
    {
        selection.SelectSingle("A");
        selection.SelectRange("D", Order);

        selection.Retain(["A", "C", "E"]);

        // B·D 만 사라졌다. 남은 선택은 건드리지 않는다 (CLAUDE.md §4).
        Assert.Equal(["A", "C"], Names());
        Assert.Equal("A", selection.Anchor);
    }

    [Fact]
    public void Retain_AnchorGone_ClearsTheAnchorAndKeepsTheRest()
    {
        selection.SelectSingle("A");
        selection.Toggle("C");

        selection.Retain(["A", "B"]);

        Assert.Equal(["A"], Names());
        Assert.Null(selection.Anchor);
    }

    [Fact]
    public void Retain_NothingDisappeared_KeepsTheSelectionAndStaysQuiet()
    {
        selection.SelectSingle("A");
        selection.Toggle("C");
        var changed = Record();

        selection.Retain(Order);

        Assert.Equal(["A", "C"], Names());
        Assert.Equal("C", selection.Anchor);
        Assert.Empty(changed);
    }

    [Fact]
    public void Retain_IgnoresCase()
    {
        selection.SelectSingle("Report.TXT");

        selection.Retain(["report.txt"]);

        Assert.Equal(["Report.TXT"], Names());
        Assert.Equal("Report.TXT", selection.Anchor);
    }

    [Fact]
    public void Retain_EverythingGone_LeavesNothingSelected()
    {
        selection.SelectSingle("A");
        selection.Toggle("B");

        selection.Retain([]);

        Assert.Equal(0, selection.Count);
        Assert.Null(selection.Anchor);
    }

    // ── reconcile 결과 반영 (ReplaceWith) ─────────────────────────

    [Fact]
    public void ReplaceWith_TakesTheGivenNames()
    {
        // 이름이 바뀐 항목의 선택이 새 이름으로 이어진다 — ListReconciler 가 그렇게 낸다.
        selection.SelectSingle("old.txt");

        selection.ReplaceWith(["new.txt"]);

        Assert.Equal(["new.txt"], Names());
        Assert.True(selection.IsSelected("new.txt"));
        Assert.False(selection.IsSelected("old.txt"));
    }

    [Fact]
    public void ReplaceWith_KeepsTheAnchorWhenItSurvived()
    {
        selection.SelectSingle("A");
        selection.Toggle("B");

        selection.ReplaceWith(["A", "B"]);

        Assert.Equal(["A", "B"], Names());
        Assert.Equal("B", selection.Anchor);
    }

    [Fact]
    public void ReplaceWith_AnchorNotInTheResult_DropsTheAnchor()
    {
        selection.SelectSingle("A");
        selection.Toggle("B");

        selection.ReplaceWith(["A"]);

        Assert.Equal(["A"], Names());
        Assert.Null(selection.Anchor);
    }

    [Fact]
    public void ReplaceWith_Empty_LeavesNothingSelected()
    {
        selection.SelectSingle("A");

        selection.ReplaceWith([]);

        Assert.Equal(0, selection.Count);
        Assert.Null(selection.Anchor);
    }

    // ── 대소문자 ──────────────────────────────────────────────────

    [Fact]
    public void IsSelected_IgnoresCase()
    {
        selection.SelectSingle("Report.TXT");

        Assert.True(selection.IsSelected("report.txt"));
        Assert.True(selection.IsSelected("REPORT.TXT"));
    }

    // ── 알림 ──────────────────────────────────────────────────────

    [Fact]
    public void SelectSingle_RaisesPropertyChangedForWhatTheStatusBarBindsTo()
    {
        var changed = Record();

        selection.SelectSingle("A");

        Assert.Contains(nameof(PaneSelection.Count), changed);
        Assert.Contains(nameof(PaneSelection.SelectedNames), changed);
    }

    [Fact]
    public void Toggle_And_Clear_RaisePropertyChanged()
    {
        selection.SelectSingle("A");
        var changed = Record();

        selection.Toggle("B");
        selection.Clear();

        Assert.Equal(2, changed.Count(name => name == nameof(PaneSelection.Count)));
        Assert.Equal(2, changed.Count(name => name == nameof(PaneSelection.SelectedNames)));
    }

    [Fact]
    public void SelectSingle_OnTheSameItem_StaysQuiet()
    {
        selection.SelectSingle("A");
        var changed = Record();

        selection.SelectSingle("A");

        Assert.Empty(changed);
    }

    [Fact]
    public void Clear_WithNothingSelected_StaysQuiet()
    {
        var changed = Record();

        selection.Clear();

        Assert.Empty(changed);
    }

    // ── 인자 ──────────────────────────────────────────────────────

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => selection.SelectSingle(null!));
        Assert.Throws<ArgumentNullException>(() => selection.Toggle(null!));
        Assert.Throws<ArgumentNullException>(() => selection.SelectRange(null!, Order));
        Assert.Throws<ArgumentNullException>(() => selection.SelectRange("A", null!));
        Assert.Throws<ArgumentNullException>(() => selection.Retain(null!));
        Assert.Throws<ArgumentNullException>(() => selection.ReplaceWith(null!));
        Assert.Throws<ArgumentNullException>(() => selection.IsSelected(null!));
    }

    [Fact]
    public void EmptyName_Throws()
    {
        // 빈 문자열은 항목 이름이 아니다. 조용히 담으면 상태표시줄 개수가 실제와 어긋난다.
        Assert.Throws<ArgumentException>(() => selection.SelectSingle(string.Empty));
        Assert.Throws<ArgumentException>(() => selection.Toggle(string.Empty));
        Assert.Throws<ArgumentException>(() => selection.SelectRange(string.Empty, Order));
        Assert.Throws<ArgumentException>(() => selection.IsSelected(string.Empty));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    /// <summary>화면 순서. <c>SelectRange</c> 는 이 순서 안에서 범위를 잡는다.</summary>
    private static IReadOnlyList<string> Order => ["A", "B", "C", "D", "E"];

    /// <summary><c>SelectedNames</c> 는 순서를 보장하지 않는다 — 비교 전에 정렬한다.</summary>
    private string[] Names() => [.. selection.SelectedNames.OrderBy(name => name, StringComparer.Ordinal)];

    private List<string?> Record()
    {
        var changed = new List<string?>();
        selection.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        return changed;
    }
}
