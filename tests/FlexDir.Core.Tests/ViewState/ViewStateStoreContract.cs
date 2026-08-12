using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.Core.Tests.ViewState;

/// <summary>
/// <see cref="IViewStateStore"/> 구현체가 반드시 만족해야 하는 성질.
/// <para>
/// 테스트 클래스가 아니라 기반 클래스다 (abstract 라서 xunit 이 수집하지 않는다).
/// <c>FlexDir.Shell</c> 의 파일 기반 구현체 테스트가 이 클래스를 상속해 같은 검증을
/// 받는 것이 존재 이유다 — 그래서 여기서는 <see cref="IViewStateStore"/> 만 본다.
/// 구현체의 내부 구조(사전·파일·직렬화 포맷)를 들여다보면 재사용이 되지 않는다.
/// </para>
/// </summary>
public abstract class ViewStateStoreContract
{
    protected abstract IViewStateStore CreateStore();

    // ── 왕복 ────────────────────────────────────────────────────────

    [Fact]
    public async Task SavedFolderState_IsLoadedBack()
    {
        var store = CreateStore();
        var folder = Folder(@"C:\Temp\Docs");
        var state = new FolderViewState(ViewMode.Tiles, [new SortOrder(SortKey.Modified, Descending: true)]);

        await store.SaveAsync(folder, state, CancellationToken.None);

        Assert.Equal(state, await store.TryLoadAsync(folder, CancellationToken.None));
    }

    // 그룹 기준과 접힌 그룹도 폴더별 기억이다 (docs/PRD-v2.md §6-1). 접힌 상태가 왕복하지
    // 않으면 폴더를 떠났다 돌아올 때마다 그룹이 전부 펼쳐진다.
    [Fact]
    public async Task SavedFolderState_KeepsGroupingAndCollapsedGroups()
    {
        var store = CreateStore();
        var folder = Folder(@"C:\Temp\Docs");
        var state = new FolderViewState(
            ViewMode.Details, [new SortOrder(SortKey.Name)], SortKey.Type, ["PNG", "확장자 없음"]);

        await store.SaveAsync(folder, state, CancellationToken.None);

        Assert.Equal(state, await store.TryLoadAsync(folder, CancellationToken.None));
    }

    [Fact]
    public async Task SavedGlobalState_IsLoadedBack()
    {
        var store = CreateStore();
        var state = new GlobalViewState(0.35, new WindowPlacement(10, 20, 1280, 800, Maximized: true));

        await store.SaveGlobalAsync(state, CancellationToken.None);

        Assert.Equal(state, await store.LoadGlobalAsync(CancellationToken.None));
    }

    // Details 컬럼 폭은 폴더가 아니라 전역이되 **페인마다 따로** 다 (사용자 지적
    // 2026-08-10) — 한쪽에서 끌 때 반대편이 함께 움직이면 안 된다.
    [Fact]
    public async Task SavedGlobalState_KeepsEachPaneColumnWidths()
    {
        var store = CreateStore();
        var state = GlobalViewState.Default with
        {
            Panes =
            [
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\A")), new PaneColumns(400, 70, 200, 180)),
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\B")), new PaneColumns(200, 60, 90, 100)),
            ],
        };

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(new PaneColumns(400, 70, 200, 180), loaded.Panes![0].Columns);
        Assert.Equal(new PaneColumns(200, 60, 90, 100), loaded.Panes[1].Columns);
    }

    [Fact]
    public async Task SavedGlobalState_WithoutColumns_LoadsTheDefaultWidths()
    {
        // 컬럼이 들어오기 전 파일이거나 방금 만든 페인이다. 없으면 기본 폭으로 뜬다 —
        // null 을 내면 읽는 쪽이 매번 기본값을 다시 고르게 된다 (PaneState.Columns).
        var store = CreateStore();

        await store.SaveGlobalAsync(
            GlobalViewState.Default with { Panes = [new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\A")))] },
            CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(PaneColumns.Default, loaded.Panes![0].Columns);
    }

    // ── 분할 (docs/PRD-v2.md §18) ───────────────────────────────────
    // 분할 상태를 기억한다 (사용자 결정 2026-08-12). 왕복하지 않으면 매 실행마다 쓰던
    // 화면을 다시 짜야 한다 — 트리를 접어 둔 것과 같은 자리다.

    [Fact]
    public async Task SavedGlobalState_KeepsTheSplitShape()
    {
        var store = CreateStore();
        var state = GlobalViewState.Default with { PaneCount = 4, SplitterRatio = 0.4, RowRatio = 0.6 };

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(4, loaded.PaneCount);
        Assert.Equal(0.4, loaded.SplitterRatio);
        Assert.Equal(0.6, loaded.RowRatio);
    }

    [Fact]
    public async Task SavedGlobalState_KeepsFoldedPanes()
    {
        // 접은 페인은 재시작을 건너서도 살아 있어야 한다 (사용자 결정 2026-08-12:
        // "다시 폄면 그대로"). 보이는 수만 저장하면 접기가 닫기가 된다.
        var store = CreateStore();
        var state = GlobalViewState.Default with
        {
            PaneCount = 1,
            Panes =
            [
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\Shown"))),
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\Folded"))),
            ],
        };

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(1, loaded.PaneCount);
        Assert.Equal(2, loaded.Panes!.Count);
        Assert.Equal(Folder(@"C:\Temp\Folded"), loaded.Panes[1].Tabs.Tabs[0].Folder);
    }

    // 트리를 접어 둔 것과 폭도 전역 상태다 (docs/PRD-v2.md §10). 왕복하지 않으면 좁은
    // 화면에서 되찾은 가로 공간을 실행할 때마다 다시 되찾아야 한다.
    [Fact]
    public async Task SavedGlobalState_KeepsTheTreeShape()
    {
        var store = CreateStore();
        var state = GlobalViewState.Default with { TreeVisible = false, TreeWidth = 300 };

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.False(loaded.TreeVisible);
        Assert.Equal(300, loaded.TreeWidth);
    }

    [Fact]
    public async Task SavedGlobalState_KeepsTheLastFolderOfEveryPane()
    {
        // 시작 폴더 복원의 근거다 — 다음 실행이 여기서 마지막 폴더를 읽는다 (phase B-2).
        var store = CreateStore();
        var state = GlobalViewState.Default with
        {
            PaneCount = 2,
            Panes =
            [
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\Left"))),
                new PaneState(PaneTabsState.Single(Folder(@"C:\Temp\Right"))),
            ],
        };

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);
        Assert.Equal(Folder(@"C:\Temp\Left"), Assert.Single(loaded.Panes![0].Tabs.Tabs).Folder);
        Assert.Equal(Folder(@"C:\Temp\Right"), Assert.Single(loaded.Panes[1].Tabs.Tabs).Folder);
    }

    [Fact]
    public async Task SavedGlobalState_WithoutFolders_LoadsPanesAsNull()
    {
        // 창을 한 번도 띄우지 않고 끝났거나 옛 파일이다 — 복원은 폴백으로 간다.
        var store = CreateStore();

        await store.SaveGlobalAsync(new GlobalViewState(0.4, null), CancellationToken.None);

        Assert.Null((await store.LoadGlobalAsync(CancellationToken.None)).Panes);
    }

    // 세션 복원은 탭 목록 전부다 — 순서·활성 탭·고정·사용자 제목까지 (docs/PRD-v2.md §17).
    // 하나라도 왕복에서 빠지면 매일 같은 자리로 돌아온다는 이 앱의 전제가 깨진다.
    [Fact]
    public async Task SavedGlobalState_KeepsEveryTabWithItsPinAndTitle()
    {
        var store = CreateStore();
        var first = new PaneTabsState(
            [
                new TabState(Folder(@"C:\Temp\Pinned"), IsPinned: true),
                new TabState(Folder(@"C:\Temp\Named"), Title: "일감"),
                new TabState(Folder(@"C:\Temp\Plain")),
            ],
            ActiveIndex: 2);

        await store.SaveGlobalAsync(
            GlobalViewState.Default with
            {
                PaneCount = 2,
                Panes = [new PaneState(first), new PaneState(PaneTabsState.Single(Folder(@"D:\")))],
            },
            CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(first, loaded.Panes![0].Tabs);
        Assert.Single(loaded.Panes[1].Tabs.Tabs);
    }

    // ── 기억이 없는 경우 ────────────────────────────────────────────
    // 처음 방문하는 폴더가 정상 상황이다. 예외가 아니다.

    [Fact]
    public async Task UnknownFolder_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(await store.TryLoadAsync(Folder(@"C:\NeverVisited"), CancellationToken.None));
    }

    [Fact]
    public async Task OtherFolderSaved_DoesNotLeakIntoThisOne()
    {
        var store = CreateStore();
        await store.SaveAsync(Folder(@"C:\A"), FolderViewState.Default, CancellationToken.None);

        Assert.Null(await store.TryLoadAsync(Folder(@"C:\B"), CancellationToken.None));
    }

    // 전역 상태는 null 을 내지 않는다 — 호출자가 기본값을 조립하면 기본값이 두 군데에 생긴다.
    [Fact]
    public async Task GlobalState_NeverSaved_ReturnsDefault()
    {
        var store = CreateStore();

        Assert.Equal(GlobalViewState.Default, await store.LoadGlobalAsync(CancellationToken.None));
    }

    // ── 폴더 키 ─────────────────────────────────────────────────────
    // Windows 파일시스템은 대소문자를 구분하지 않는다. 주소창에 소문자로 입력했다고
    // 뷰 설정이 따로 생기면 "기억한다" 가 성립하지 않는다.

    [Fact]
    public async Task PathsDifferingOnlyInCase_AreTheSameFolder()
    {
        var store = CreateStore();
        var state = new FolderViewState(ViewMode.LargeIcons, [new SortOrder(SortKey.Size)]);

        await store.SaveAsync(Folder(@"C:\Temp\Photos"), state, CancellationToken.None);

        Assert.Equal(state, await store.TryLoadAsync(Folder(@"c:\temp\photos"), CancellationToken.None));
    }

    [Fact]
    public async Task SavingTwice_KeepsTheLastValue()
    {
        var store = CreateStore();
        var folder = Folder(@"C:\Temp\Docs");
        var last = new FolderViewState(ViewMode.List, [new SortOrder(SortKey.Type)]);

        await store.SaveAsync(folder, FolderViewState.Default, CancellationToken.None);
        await store.SaveAsync(folder, last, CancellationToken.None);

        Assert.Equal(last, await store.TryLoadAsync(folder, CancellationToken.None));
    }

    [Fact]
    public async Task SavingGlobalTwice_KeepsTheLastValue()
    {
        var store = CreateStore();
        var last = new GlobalViewState(0.7, null);

        await store.SaveGlobalAsync(new GlobalViewState(0.2, null), CancellationToken.None);
        await store.SaveGlobalAsync(last, CancellationToken.None);

        Assert.Equal(last, await store.LoadGlobalAsync(CancellationToken.None));
    }

    // ── 취소 ────────────────────────────────────────────────────────
    // 저장 위치가 네트워크 드라이브면 초 단위로 걸린다. 취소를 무시하는 구현은
    // UI 를 붙잡는다 (CLAUDE.md §3).

    [Fact]
    public async Task CanceledToken_TryLoad_Throws()
    {
        var store = CreateStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.TryLoadAsync(Folder(@"C:\Temp"), Canceled()));
    }

    [Fact]
    public async Task CanceledToken_Save_Throws()
    {
        var store = CreateStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.SaveAsync(Folder(@"C:\Temp"), FolderViewState.Default, Canceled()));
    }

    [Fact]
    public async Task CanceledToken_LoadGlobal_Throws()
    {
        var store = CreateStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.LoadGlobalAsync(Canceled()));
    }

    [Fact]
    public async Task CanceledToken_SaveGlobal_Throws()
    {
        var store = CreateStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.SaveGlobalAsync(GlobalViewState.Default, Canceled()));
    }

    private static CancellationToken Canceled() => new(canceled: true);

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }
}
