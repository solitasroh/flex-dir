using FlexDir.Core.Favorites;
using FlexDir.Core.Locations;
using FlexDir.Shell.Favorites;

using Xunit;

namespace FlexDir.Shell.Tests.Favorites;

/// <summary>
/// 즐겨찾기를 파일 하나에 담는 <see cref="JsonFavoriteStore"/>.
/// <para>
/// 뷰 상태와 <b>다른 파일</b>이다 — 뷰 상태가 깨져서 기본값으로 접히는 사건이 즐겨찾기를
/// 함께 지우면 안 된다. 그리고 깨진 파일을 조용히 덮어쓰지 않는다: 사용자가 모아 둔
/// 목록은 파일시스템 어디에도 없어서 되찾을 곳이 없다.
/// </para>
/// </summary>
public class JsonFavoriteStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flexdir-fav-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private JsonFavoriteStore CreateStore() => new(directory);

    private string FilePath => Path.Combine(directory, JsonFavoriteStore.FileName);

    private void WriteRawFile(string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, text);
    }

    private static LocationId Loc(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task LoadAsync_WithNoFile_IsEmpty()
    {
        Assert.Empty(await CreateStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SavedFavorites_AreLoadedBackInOrder()
    {
        var store = CreateStore();
        Favorite[] favorites =
        [
            new(Loc(@"C:\work"), "작업"),
            new(Loc(@"\\10.10.10.23\home"), "NAS"),
        ];

        await store.SaveAsync(favorites, CancellationToken.None);

        Assert.Equal(favorites, await CreateStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_WritesItsOwnFile()
    {
        // 뷰 상태 파일과 섞이면 그쪽이 깨질 때 함께 날아간다.
        await CreateStore().SaveAsync([new Favorite(Loc(@"C:\work"), "작업")], CancellationToken.None);

        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(Path.Combine(directory, "view-state.json")));
    }

    [Fact]
    public async Task SaveAsync_StoresTheDisplayPath()
    {
        // 사람이 열어 고칠 수 있어야 한다 — \\?\ 접두사가 붙어 있으면 붙여 넣지도 못한다.
        await CreateStore().SaveAsync([new Favorite(Loc(@"C:\work"), "작업")], CancellationToken.None);

        Assert.Contains(@"C:\\work", await File.ReadAllTextAsync(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithABrokenFile_KeepsTheOriginalAside()
    {
        // 깨진 파일을 빈 목록으로 읽고 다음 저장이 그대로 덮어쓰면 사용자가 모아 둔 것이
        // 영영 사라진다. 되찾을 곳이 없으므로 원본을 옆에 남긴다.
        WriteRawFile("{ 이건 JSON 이 아니다");

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Empty(loaded);
        Assert.True(File.Exists(FilePath + ".bak"), "깨진 파일이 보존되지 않았다.");
    }

    [Fact]
    public async Task LoadAsync_SkipsEntriesThatDoNotParse()
    {
        // 손으로 고치다 경로 하나가 깨질 수 있다. 그 하나 때문에 나머지를 잃지 않는다.
        WriteRawFile("""
            { "favorites": [
                { "path": "??", "label": "깨진 것" },
                { "path": "C:\\work", "label": "작업" }
            ] }
            """);

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(["작업"], loaded.Select(favorite => favorite.Label));
    }

    [Fact]
    public async Task LoadAsync_WithAMissingLabel_FallsBackToTheFolderName()
    {
        // 라벨이 비면 트리에 빈 줄이 선다.
        WriteRawFile("""
            { "favorites": [ { "path": "C:\\work" } ] }
            """);

        var favorite = Assert.Single(await CreateStore().LoadAsync(CancellationToken.None));

        Assert.Equal("work", favorite.Label);
    }

    [Fact]
    public async Task SaveAsync_Twice_ReplacesTheWholeList()
    {
        var store = CreateStore();

        await store.SaveAsync([new Favorite(Loc(@"C:\a"), "A")], CancellationToken.None);
        await store.SaveAsync([new Favorite(Loc(@"C:\b"), "B")], CancellationToken.None);

        Assert.Equal(["B"], (await store.LoadAsync(CancellationToken.None)).Select(f => f.Label));
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTemporaryFileBehind()
    {
        // 임시 파일에 쓰고 옮긴다 — 쓰다 죽어도 반쪽 파일이 정본이 되지 않는다.
        await CreateStore().SaveAsync([new Favorite(Loc(@"C:\a"), "A")], CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateStore().SaveAsync([], cts.Token));
    }
}
