using FlexDir.Core.Enumeration;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Sorting;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// 외부 변경을 목록에 병합한다. <b>선택 유지가 이 함수의 존재 이유다</b> —
/// "갱신 때마다 선택이 풀리면 감시가 없느니만 못하다" (ADR-011 · CLAUDE.md §4).
/// <para>
/// 그래서 여기서 가장 중요한 테스트는
/// <see cref="RenamingASelectedItem_CarriesTheSelectionToTheNewName"/> 다. 이름 변경을
/// 제거 + 추가로 처리하는 구현은 그것만 어긴다 — 목록은 맞고 선택만 조용히 사라진다.
/// </para>
/// <para>
/// 파일시스템은 읽지 않는다. 다시 읽어온 항목은 <c>upserts</c> 로 들어오므로 이 테스트에는
/// 임시 폴더도 정리 코드도 없다.
/// </para>
/// </summary>
public class ListReconcilerTests
{
    private static readonly LocationId Docs = Folder(@"C:\Temp\Docs");

    // ── 목록 병합 ───────────────────────────────────────────────────

    [Fact]
    public void UpsertedItem_LandsInItsSortedPosition()
    {
        // 맨 끝에 붙이면 다음 정렬까지 목록이 어긋난 채로 보인다.
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: [],
            upserts: [Item("b.txt")]);

        Assert.Equal(["a.txt", "b.txt", "c.txt"], Names(result));
    }

    [Fact]
    public void UpsertedFolder_StaysAboveTheFiles()
    {
        // 정렬은 넘겨받은 비교기가 정한다. 폴더 먼저는 FileItemComparer 의 규칙이다.
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: [],
            upserts: [Dir("zz")]);

        Assert.Equal(["zz", "a.txt", "c.txt"], Names(result));
    }

    [Fact]
    public void UpsertingAnExistingName_ReplacesItWithoutDuplicating()
    {
        var result = Apply(
            current: [Item("a.txt", size: 1), Item("b.txt")],
            selection: [],
            upserts: [Item("a.txt", size: 4096)]);

        Assert.Equal(["a.txt", "b.txt"], Names(result));
        Assert.Equal(4096, result.Items.Single(item => item.Name == "a.txt").Size);
    }

    [Fact]
    public void RemovedItem_LeavesTheList()
    {
        var result = Apply(
            current: [Item("a.txt"), Item("b.txt")],
            selection: [],
            removals: ["a.txt"]);

        Assert.Equal(["b.txt"], Names(result));
    }

    [Fact]
    public void RenamedItem_KeepsItsPlaceUnderTheNewName()
    {
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: [],
            renames: [("a.txt", "b.txt")]);

        // 옛 이름이 남아 있으면 같은 파일이 두 줄이 된다.
        Assert.Equal(["b.txt", "c.txt"], Names(result));
    }

    [Fact]
    public void RenamedItem_RederivesTheExtensionFromTheNewName()
    {
        // 유형 컬럼과 아이콘이 이 값을 키로 쓴다. record 의 복사 생성자는 초기화식을
        // 다시 돌리지 않으므로 `with` 로 이름만 갈면 확장자가 옛 이름으로 남는다.
        var result = Apply(
            current: [Item("report.txt")],
            selection: [],
            renames: [("report.txt", "report.md")]);

        var renamed = result.Items.Single();

        Assert.Equal("report.md", renamed.Name);
        Assert.Equal("md", renamed.Extension);

        // 항목의 위치도 새 이름을 따라간다. 어긋나면 활성화가 없는 파일을 연다.
        Assert.Equal(Docs.Combine("report.md"), renamed.Location);
    }

    [Fact]
    public void RenameIsAppliedBeforeUpsert_SoTheOldNameCannotSurvive()
    {
        // 감시는 이름 변경과 함께 새 이름의 갱신을 낸다 (ChangeBatch 규칙 5).
        // upsert 를 먼저 넣으면 옛 이름이 목록에 남아 중복이 된다.
        var result = Apply(
            current: [Item("a.txt")],
            selection: [],
            upserts: [Item("b.txt", size: 7)],
            renames: [("a.txt", "b.txt")]);

        Assert.Equal(["b.txt"], Names(result));
        Assert.Equal(7, result.Items.Single().Size);
    }

    [Fact]
    public void RenameThenRemoval_RemovesTheItem()
    {
        // 이름을 바꾼 직후 지운 경우. 제거는 새 이름으로 들어온다.
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: [],
            removals: ["b.txt"],
            renames: [("a.txt", "b.txt")]);

        Assert.Equal(["c.txt"], Names(result));
    }

    [Fact]
    public void NamesDifferingOnlyInCase_AreTheSameItem()
    {
        // Windows 파일시스템은 대소문자를 구분하지 않는다. 갈라지면 같은 파일이 두 줄이 된다.
        var result = Apply(
            current: [Item("Report.txt")],
            selection: ["report.TXT"],
            upserts: [Item("REPORT.TXT", size: 12)]);

        var single = result.Items.Single();

        Assert.Equal(12, single.Size);

        // 선택은 결과 목록의 표기로 낸다 — 표기가 두 곳에서 갈리면 View 가 어느 쪽으로
        // 비교하느냐에 따라 선택이 사라진다.
        Assert.Equal([single.Name], result.Selection);
    }

    [Fact]
    public void RemovalMatchesRegardlessOfCase()
    {
        var result = Apply(
            current: [Item("Report.txt")],
            selection: ["Report.txt"],
            removals: ["REPORT.TXT"]);

        Assert.Empty(result.Items);
        Assert.Empty(result.Selection);
    }

    // ── 선택 유지 (이 step 의 존재 이유) ────────────────────────────

    [Fact]
    public void RenamingASelectedItem_CarriesTheSelectionToTheNewName()
    {
        // ADR-011 의 핵심. 이름 변경을 제거 + 추가로 처리하면 여기서만 깨진다.
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: ["a.txt"],
            renames: [("a.txt", "b.txt")]);

        Assert.Equal(["b.txt"], result.Selection);
    }

    [Fact]
    public void RenamingASelectedItem_CarriesTheSelectionEvenWhenTheUpsertFollows()
    {
        // 실제 경로는 이쪽이다 — 감시가 rename 과 새 이름의 갱신을 함께 낸다.
        var result = Apply(
            current: [Item("a.txt")],
            selection: ["a.txt"],
            upserts: [Item("b.txt")],
            renames: [("a.txt", "b.txt")]);

        Assert.Equal(["b.txt"], result.Selection);
    }

    [Fact]
    public void RenamingAnUnselectedItem_DoesNotSelectIt()
    {
        var result = Apply(
            current: [Item("a.txt"), Item("c.txt")],
            selection: ["c.txt"],
            renames: [("a.txt", "b.txt")]);

        Assert.Equal(["c.txt"], result.Selection);
    }

    [Fact]
    public void RemovedItem_LeavesTheSelectionToo()
    {
        var result = Apply(
            current: [Item("a.txt"), Item("b.txt")],
            selection: ["a.txt", "b.txt"],
            removals: ["a.txt"]);

        Assert.Equal(["b.txt"], Names(result));
        Assert.Equal(["b.txt"], result.Selection);
    }

    [Fact]
    public void ChangingAnUnselectedItem_LeavesTheSelectionAlone()
    {
        var result = Apply(
            current: [Item("a.txt"), Item("b.txt")],
            selection: ["b.txt"],
            upserts: [Item("a.txt", size: 99), Item("c.txt")],
            removals: ["d.txt"]);

        // 목록이 바뀌었다고 선택을 비우지 않는다.
        Assert.Equal(["b.txt"], result.Selection);
    }

    [Fact]
    public void Selection_HoldsOnlyNamesThatExistInTheResult()
    {
        // 없는 이름이 선택에 남으면 상태 표시줄 개수가 실제와 어긋난다.
        var result = Apply(
            current: [Item("a.txt")],
            selection: ["a.txt", "ghost.txt"],
            removals: []);

        Assert.Equal(["a.txt"], result.Selection);
    }

    [Fact]
    public void EmptyChangeSet_LeavesBothTheListAndTheSelectionAlone()
    {
        var current = new[] { Dir("sub"), Item("a.txt"), Item("b.txt") };

        var result = Apply(current, selection: ["b.txt", "sub"]);

        Assert.Equal(["sub", "a.txt", "b.txt"], Names(result));

        // 선택 순서는 결과 목록의 순서다 — 집합이므로 순서 자체는 계약이 아니지만
        // 결정적이어야 테스트가 흔들리지 않는다.
        Assert.Equal(["sub", "b.txt"], result.Selection);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    /// <summary>
    /// 정렬 기준은 이름 오름차순·폴더 먼저 (<see cref="FileItemComparer.Default"/>).
    /// 넘기지 않은 변경 목록은 비어 있다.
    /// </summary>
    private static ReconcileResult Apply(
        IReadOnlyList<FileItem> current,
        IReadOnlyCollection<string> selection,
        IReadOnlyList<FileItem>? upserts = null,
        IReadOnlyCollection<string>? removals = null,
        IReadOnlyList<(string OldName, string NewName)>? renames = null)
        => ListReconciler.Apply(
            current,
            selection,
            upserts ?? [],
            removals ?? [],
            renames ?? [],
            FileItemComparer.Default);

    private static IEnumerable<string> Names(ReconcileResult result)
        => result.Items.Select(item => item.Name);

    private static FileItem Item(string name, long size = 0)
        => new(name, Docs.Combine(name), size, DateTimeOffset.UnixEpoch, FileItemFlags.None);

    private static FileItem Dir(string name)
        => new(name, Docs.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory);

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }
}
