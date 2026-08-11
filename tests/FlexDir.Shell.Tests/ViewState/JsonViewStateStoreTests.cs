using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.Tests.ViewState;
using FlexDir.Core.ViewState;
using FlexDir.Shell.ViewState;

using Xunit;

namespace FlexDir.Shell.Tests.ViewState;

/// <summary>
/// 파일 기반 <see cref="IViewStateStore"/> 구현체.
/// <para>
/// <see cref="ViewStateStoreContract"/> 를 상속해 fake 가 받던 검증을 실물도 그대로 받는다.
/// 아래에 더 붙는 것은 <b>파일이라서 생기는 성질</b>뿐이다 — 프로세스를 다시 띄웠을 때
/// 읽히는가, 파일이 깨졌을 때 어떻게 되는가, 쓰다 죽었을 때 무엇이 남는가.
/// </para>
/// </summary>
public sealed class JsonViewStateStoreTests : ViewStateStoreContract, IDisposable
{
    // 테스트마다 새 인스턴스가 만들어지므로 폴더 하나가 곧 격리다.
    // 일부러 만들지 않는다 — "저장 위치가 없으면 만든다" 도 검증 대상이다.
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "flex-dir-tests", Guid.NewGuid().ToString("N"));

    protected override IViewStateStore CreateStore() => new JsonViewStateStore(root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 임시 폴더 청소 실패는 테스트 결과와 무관하다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── 파일이라서 생기는 성질 ──────────────────────────────────────

    // 이 테스트가 없으면 메모리 사전만 들고 있는 구현체도 계약을 통과한다.
    // 폴더별 기억(docs/PRD.md §2)은 앱을 다시 띄운 뒤에도 남아야 한다.
    [Fact]
    public async Task NewInstance_ReadsWhatThePreviousOneWrote()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var state = new FolderViewState(ViewMode.Tiles, [new SortOrder(SortKey.Size, Descending: true)]);

        await new JsonViewStateStore(root).SaveAsync(folder, state, CancellationToken.None);

        Assert.Equal(state, await new JsonViewStateStore(root).TryLoadAsync(folder, CancellationToken.None));
    }

    [Fact]
    public async Task GlobalState_SurvivesANewInstance()
    {
        var state = new GlobalViewState(0.7, new WindowPlacement(1, 2, 900, 560, Maximized: false));

        await new JsonViewStateStore(root).SaveGlobalAsync(state, CancellationToken.None);

        Assert.Equal(state, await new JsonViewStateStore(root).LoadGlobalAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingDirectory_IsCreatedOnSave()
    {
        Assert.False(Directory.Exists(root));

        await CreateStore().SaveGlobalAsync(GlobalViewState.Default, CancellationToken.None);

        Assert.True(Directory.Exists(root));
    }

    // ── 깨진 파일 ───────────────────────────────────────────────────
    // 뷰 상태는 캐시다 (CLAUDE.md §4). 읽지 못하면 기본값으로 가야 하고, 목록을 막아서는
    // 안 된다 — 폴더를 못 여는 것과 뷰 설정을 못 읽는 것은 사용자에게 전혀 다른 사건이다.

    [Fact]
    public async Task CorruptFile_FolderStateReadsAsUnknown()
    {
        WriteRawFile("{ 이건 JSON 이 아니다");

        Assert.Null(await CreateStore().TryLoadAsync(Folder(@"C:\Temp\Docs"), CancellationToken.None));
    }

    [Fact]
    public async Task CorruptFile_GlobalStateFallsBackToDefault()
    {
        WriteRawFile("{ 이건 JSON 이 아니다");

        Assert.Equal(GlobalViewState.Default, await CreateStore().LoadGlobalAsync(CancellationToken.None));
    }

    // FolderViewState 는 빈 정렬을 거부한다. 파일에 그런 값이 들어 있으면 역직렬화가
    // 예외를 던지는데, 그것이 밖으로 새면 폴더를 여는 길이 막힌다.
    [Fact]
    public async Task FileWithEmptySort_ReadsAsUnknownInsteadOfThrowing()
    {
        WriteRawFile("""
            { "folders": { "\\\\?\\C:\\TEMP\\DOCS": { "mode": "Tiles", "sort": [] } } }
            """);

        Assert.Null(await CreateStore().TryLoadAsync(Folder(@"C:\Temp\Docs"), CancellationToken.None));
    }

    // v1 이 쓴 파일에는 groupBy·collapsed 가 아예 없다. 그것을 읽지 못하면 도그푸딩 중인
    // 기계에서 폴더별 기억이 통째로 날아간다 (docs/PRD-v2.md §6-1 저장 호환).
    [Fact]
    public async Task FileWrittenBeforeGrouping_LoadsWithGroupingOff()
    {
        WriteRawFile("""
            { "folders": { "\\\\?\\C:\\TEMP\\DOCS": { "mode": "Tiles", "sort": [ { "key": "Size", "descending": true } ] } } }
            """);

        var loaded = await CreateStore().TryLoadAsync(Folder(@"C:\Temp\Docs"), CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(ViewMode.Tiles, loaded.Mode);
        Assert.Equal([new SortOrder(SortKey.Size, Descending: true)], loaded.Sort);
        Assert.Null(loaded.GroupBy);
        Assert.Empty(loaded.Collapsed);
    }

    // 트리가 들어오기 전에 쓴 파일에는 tree 필드가 없다. 그것을 못 읽으면 창 배치와
    // 마지막 폴더까지 함께 날아간다 (docs/PRD-v2.md §10 저장 호환).
    [Fact]
    public async Task FileWrittenBeforeTheTree_LoadsWithTheTreeShown()
    {
        WriteRawFile("""
            { "global": { "splitterRatio": 0.4, "leftFolder": "C:\\Temp" } }
            """);

        var loaded = await CreateStore().LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(0.4, loaded.SplitterRatio);
        Assert.NotNull(loaded.LeftTabs);
        Assert.True(loaded.TreeVisible);
        Assert.Equal(GlobalViewState.DefaultTreeWidth, loaded.TreeWidth);
    }

    // 탭이 들어오기 전에 쓴 파일에는 leftFolder·rightFolder 가 단수다. 그 변환은 여기서
    // 끝나고 ViewModel 은 모른다 (docs/PRD-v2.md §17 저장 호환) — 못 읽으면 도그푸딩 중인
    // 기계가 다음 실행에 빈 페인으로 뜬다.
    [Fact]
    public async Task FileWrittenBeforeTabs_ReadsEachFolderAsASingleTab()
    {
        WriteRawFile("""
            { "global": { "splitterRatio": 0.4, "leftFolder": "C:\\Temp", "rightFolder": "D:\\" } }
            """);

        var loaded = await CreateStore().LoadGlobalAsync(CancellationToken.None);

        var left = Assert.Single(loaded.LeftTabs!.Tabs);
        Assert.Equal(Folder(@"C:\Temp"), left.Folder);
        Assert.False(left.IsPinned);
        Assert.Null(left.Title);
        Assert.Equal(0, loaded.LeftTabs.ActiveIndex);

        Assert.Equal(Folder(@"D:\"), Assert.Single(loaded.RightTabs!.Tabs).Folder);
    }

    // 한쪽만 기억이 있는 파일. 없는 쪽을 "탭 0개" 로 읽으면 그 페인이 시작 폴더 규칙을
    // 지나지 못하고 빈 채로 뜬다 — 기억이 없는 것은 null 이다.
    [Fact]
    public async Task FileWrittenBeforeTabs_WithOnlyOneFolder_LeavesTheOtherPaneUnremembered()
    {
        WriteRawFile("""
            { "global": { "splitterRatio": 0.4, "leftFolder": "C:\\Temp" } }
            """);

        var loaded = await CreateStore().LoadGlobalAsync(CancellationToken.None);

        Assert.NotNull(loaded.LeftTabs);
        Assert.Null(loaded.RightTabs);
    }

    // 새 형식과 옛 형식이 한 파일에 같이 있으면 새 것이 이긴다 — 옛 필드는 우리가 더 이상
    // 쓰지 않으므로 남아 있다면 그것이 낡은 값이다.
    [Fact]
    public async Task FileWithBothShapes_PrefersTheTabList()
    {
        WriteRawFile("""
            {
              "global": {
                "splitterRatio": 0.4,
                "leftFolder": "C:\\Old",
                "leftTabs": { "tabs": [ { "folder": "C:\\New" } ], "activeIndex": 0 }
              }
            }
            """);

        var loaded = await CreateStore().LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(Folder(@"C:\New"), Assert.Single(loaded.LeftTabs!.Tabs).Folder);
    }

    // 경로 하나가 깨졌다고 나머지 탭까지 잃으면 안 된다 — 폴더별 기억을 한 폴더의 손상으로
    // 통째로 버리지 않는 것과 같은 판단이다.
    [Fact]
    public async Task FileWithOneUnparsableTabPath_KeepsTheOtherTabs()
    {
        WriteRawFile("""
            {
              "global": {
                "splitterRatio": 0.4,
                "leftTabs": { "tabs": [ { "folder": "" }, { "folder": "C:\\Good" } ], "activeIndex": 1 }
              }
            }
            """);

        var loaded = await CreateStore().LoadGlobalAsync(CancellationToken.None);

        Assert.Equal(Folder(@"C:\Good"), Assert.Single(loaded.LeftTabs!.Tabs).Folder);

        // 앞의 탭이 빠졌으니 번호도 함께 당겨져야 한다. 그대로 두면 활성 탭이 목록 밖이다.
        Assert.Equal(0, loaded.LeftTabs.ActiveIndex);
    }

    // 탭이 전부 깨진 페인은 "기억이 없다" 로 간다 — 탭 0개짜리 목록을 내면 ViewModel 이
    // 그것을 복원된 상태로 받아 시작 폴더 규칙을 지나지 못한다.
    [Fact]
    public async Task FileWithOnlyUnparsableTabPaths_LeavesThePaneUnremembered()
    {
        WriteRawFile("""
            { "global": { "splitterRatio": 0.4, "leftTabs": { "tabs": [ { "folder": "" } ] } } }
            """);

        Assert.Null((await CreateStore().LoadGlobalAsync(CancellationToken.None)).LeftTabs);
    }

    // 저장 파일이 손상돼 -3 이 들어와도 페인이 사라지면 안 된다 (GlobalViewState 가 거부한다).
    [Fact]
    public async Task FileWithOutOfRangeSplitter_FallsBackToDefault()
    {
        WriteRawFile("""
            { "global": { "splitterRatio": -3 } }
            """);

        Assert.Equal(GlobalViewState.Default, await CreateStore().LoadGlobalAsync(CancellationToken.None));
    }

    // 깨진 파일을 읽은 뒤에도 저장은 되어야 한다. 그러지 않으면 한 번 깨진 파일이
    // 영구히 남아 뷰 설정이 다시는 기억되지 않는다.
    [Fact]
    public async Task SaveAfterCorruptFile_Recovers()
    {
        WriteRawFile("{ 이건 JSON 이 아니다");
        var store = CreateStore();
        var folder = Folder(@"C:\Temp\Docs");

        await store.SaveAsync(folder, FolderViewState.Default, CancellationToken.None);

        Assert.Equal(FolderViewState.Default, await store.TryLoadAsync(folder, CancellationToken.None));
    }

    // ── 쓰기 ────────────────────────────────────────────────────────

    // 제자리에서 덮어쓰면 쓰다 죽었을 때 반쪽 파일이 남고, 그 파일은 다음 실행에서
    // 손상으로 읽혀 폴더별 기억이 통째로 날아간다.
    [Fact]
    public async Task Save_LeavesNoTemporaryFileBehind()
    {
        await CreateStore().SaveGlobalAsync(GlobalViewState.Default, CancellationToken.None);

        Assert.Single(Directory.GetFiles(root));
    }

    // 사람이 열어 고칠 수 있어야 한다. 숫자로 저장하면 enum 순서를 바꾸는 순간
    // 저장된 뷰가 조용히 다른 뷰로 바뀐다.
    [Fact]
    public async Task Save_WritesEnumsAsNames()
    {
        var store = CreateStore();
        await store.SaveAsync(
            Folder(@"C:\Temp\Docs"),
            new FolderViewState(ViewMode.LargeIcons, [new SortOrder(SortKey.Modified)]),
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(Directory.GetFiles(root)[0]);

        Assert.Contains("LargeIcons", text, StringComparison.Ordinal);
        Assert.Contains("Modified", text, StringComparison.Ordinal);
    }

    private void WriteRawFile(string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, JsonViewStateStore.FileName), content);
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }
}
