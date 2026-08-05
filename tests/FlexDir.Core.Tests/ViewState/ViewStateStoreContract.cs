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

    [Fact]
    public async Task SavedGlobalState_IsLoadedBack()
    {
        var store = CreateStore();
        var state = new GlobalViewState(0.35, new WindowPlacement(10, 20, 1280, 800, Maximized: true));

        await store.SaveGlobalAsync(state, CancellationToken.None);

        Assert.Equal(state, await store.LoadGlobalAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SavedGlobalState_KeepsTheLastFoldersOfBothPanes()
    {
        // 시작 폴더 복원의 근거다 — 다음 실행이 여기서 마지막 폴더를 읽는다 (phase B-2).
        var store = CreateStore();
        var state = new GlobalViewState(
            0.5, null, Folder(@"C:\Temp\Left"), Folder(@"C:\Temp\Right"));

        await store.SaveGlobalAsync(state, CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);
        Assert.Equal(Folder(@"C:\Temp\Left"), loaded.LeftFolder);
        Assert.Equal(Folder(@"C:\Temp\Right"), loaded.RightFolder);
    }

    [Fact]
    public async Task SavedGlobalState_WithoutFolders_LoadsThemAsNull()
    {
        // 창을 한 번도 띄우지 않고 끝났거나 옛 파일이다 — 복원은 폴백으로 간다.
        var store = CreateStore();

        await store.SaveGlobalAsync(new GlobalViewState(0.4, null), CancellationToken.None);

        var loaded = await store.LoadGlobalAsync(CancellationToken.None);
        Assert.Null(loaded.LeftFolder);
        Assert.Null(loaded.RightFolder);
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
