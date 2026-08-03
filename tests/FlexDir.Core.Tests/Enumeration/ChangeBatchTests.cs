using FlexDir.Core.Enumeration;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// 감시 알림 묶음을 "무엇을 다시 읽어야 하는가" 로 번역한다. 순수 함수이며 I/O 는 없다.
/// <para>
/// 여기서 못을 박는 것은 세 가지다. (1) <see cref="FolderChangeKind.Overflow"/> 는 나머지를
/// 지운다 — 전체를 다시 읽을 것이므로 개별 목록은 낭비이고, 유실된 이벤트 때문에 어차피
/// 불완전하다. (2) 이름 변경은 제거가 아니다 — <c>OldName</c> 을 <c>Removals</c> 에 넣으면
/// 선택이 풀린다 (ADR-011). (3) 같은 이름은 한 번으로 합치고 마지막 상태가 이긴다.
/// </para>
/// </summary>
public class ChangeBatchTests
{
    // ── 번역 ────────────────────────────────────────────────────────

    [Fact]
    public void AddedAndChanged_NeedRefresh()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Added, "new.txt"),
            new FolderChange(FolderChangeKind.Changed, "old.txt"),
        ]);

        Assert.Equal(["new.txt", "old.txt"], batch.NeedsRefresh);
        Assert.Empty(batch.Removals);
        Assert.Empty(batch.Renames);
        Assert.False(batch.RequiresFullRefresh);
    }

    [Fact]
    public void Removed_GoesToRemovals()
    {
        var batch = ChangeBatch.From([new FolderChange(FolderChangeKind.Removed, "gone.txt")]);

        Assert.Equal(["gone.txt"], batch.Removals);
        Assert.Empty(batch.NeedsRefresh);
    }

    [Fact]
    public void EmptyChanges_TranslateToNothing()
    {
        var batch = ChangeBatch.From([]);

        Assert.Empty(batch.NeedsRefresh);
        Assert.Empty(batch.Removals);
        Assert.Empty(batch.Renames);
        Assert.False(batch.RequiresFullRefresh);
    }

    // ── 이름 변경 ───────────────────────────────────────────────────

    [Fact]
    public void Renamed_KeepsTheOldNameOutOfRemovals()
    {
        // 이름 변경을 제거 + 추가로 쪼개면 선택이 풀린다. Renames 를 따로 받는 이유다.
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Renamed, "new.txt", "old.txt"),
        ]);

        Assert.Empty(batch.Removals);
        Assert.Equal([("old.txt", "new.txt")], batch.Renames);
    }

    [Fact]
    public void Renamed_NeedsRefreshOfTheNewName()
    {
        // 이름이 바뀌면 확장자·유형·아이콘이 바뀐다.
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Renamed, "report.md", "report.txt"),
        ]);

        Assert.Equal(["report.md"], batch.NeedsRefresh);
    }

    [Fact]
    public void RenameChain_KeepsItsOrder()
    {
        // a → b → c. 순서를 잃으면 병합이 옛 이름을 찾지 못해 중복이 생긴다.
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Renamed, "b.txt", "a.txt"),
            new FolderChange(FolderChangeKind.Renamed, "c.txt", "b.txt"),
        ]);

        Assert.Equal([("a.txt", "b.txt"), ("b.txt", "c.txt")], batch.Renames);
    }

    [Fact]
    public void TheSameRename_IsCollapsedToOne()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Renamed, "new.txt", "old.txt"),
            new FolderChange(FolderChangeKind.Renamed, "NEW.txt", "OLD.txt"),
        ]);

        Assert.Equal([("old.txt", "new.txt")], batch.Renames);
        Assert.Equal(["new.txt"], batch.NeedsRefresh);
    }

    // ── 합치기 · 마지막 상태 ────────────────────────────────────────

    [Fact]
    public void TheSameName_IsCollapsedToOne()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Changed, "busy.txt"),
            new FolderChange(FolderChangeKind.Changed, "busy.txt"),
            new FolderChange(FolderChangeKind.Added, "busy.txt"),
        ]);

        // 저장 한 번에 Changed 가 여러 번 오는 것은 흔하다. 그만큼 다시 읽을 이유는 없다.
        Assert.Equal(["busy.txt"], batch.NeedsRefresh);
    }

    [Fact]
    public void NamesDifferingOnlyInCase_AreTheSameName()
    {
        // Windows 파일시스템은 대소문자를 구분하지 않는다.
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Added, "Report.txt"),
            new FolderChange(FolderChangeKind.Changed, "REPORT.TXT"),
        ]);

        Assert.Single(batch.NeedsRefresh);
    }

    [Fact]
    public void AddedThenRemoved_EndsUpRemoved()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Added, "temp.tmp"),
            new FolderChange(FolderChangeKind.Removed, "temp.tmp"),
        ]);

        Assert.Equal(["temp.tmp"], batch.Removals);
        Assert.Empty(batch.NeedsRefresh);
    }

    [Fact]
    public void RemovedThenAdded_EndsUpNeedingRefresh()
    {
        // 덮어쓰기 저장은 삭제 후 생성으로 나타난다. 마지막 상태가 이겨야 항목이 살아남는다.
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Removed, "doc.txt"),
            new FolderChange(FolderChangeKind.Added, "doc.txt"),
        ]);

        Assert.Equal(["doc.txt"], batch.NeedsRefresh);
        Assert.Empty(batch.Removals);
    }

    [Fact]
    public void NeedsRefresh_KeepsTheOrderOfFirstAppearance()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Added, "c.txt"),
            new FolderChange(FolderChangeKind.Added, "a.txt"),
            new FolderChange(FolderChangeKind.Changed, "c.txt"),
        ]);

        Assert.Equal(["c.txt", "a.txt"], batch.NeedsRefresh);
    }

    // ── 오버플로 ────────────────────────────────────────────────────

    [Fact]
    public void Overflow_RequiresAFullRefreshAndDiscardsTheRest()
    {
        var batch = ChangeBatch.From([
            new FolderChange(FolderChangeKind.Added, "new.txt"),
            new FolderChange(FolderChangeKind.Removed, "gone.txt"),
            new FolderChange(FolderChangeKind.Renamed, "b.txt", "a.txt"),
            FolderChange.Overflowed,
            new FolderChange(FolderChangeKind.Changed, "after.txt"),
        ]);

        Assert.True(batch.RequiresFullRefresh);

        // 개별 목록을 들고 다니면 '부분 처리로 충분하다' 는 착각이 된다. 유실된 이벤트
        // 때문에 어차피 불완전하고, 전체를 다시 읽으면 그 목록은 낭비다.
        Assert.Empty(batch.NeedsRefresh);
        Assert.Empty(batch.Removals);
        Assert.Empty(batch.Renames);
    }

    [Fact]
    public void WithoutOverflow_NoFullRefreshIsRequired()
    {
        var batch = ChangeBatch.From([new FolderChange(FolderChangeKind.Added, "new.txt")]);

        Assert.False(batch.RequiresFullRefresh);
    }
}
