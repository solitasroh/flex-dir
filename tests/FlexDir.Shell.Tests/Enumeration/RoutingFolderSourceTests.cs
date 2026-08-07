using FlexDir.Core.Enumeration;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Shell.Enumeration;

using Xunit;

namespace FlexDir.Shell.Tests.Enumeration;

/// <summary>
/// 서버(<c>\\server</c>)와 폴더는 열거 방식이 완전히 다르다 — <c>WNetEnumResource</c> 와
/// <c>FileSystemEnumerator</c> 다 (docs/PRD-v2.md §5 N-3). 한 클래스에 두 기제를 섞지 않고
/// 라우팅을 따로 세운다 — ADR-014 가 PIDL 열거에 대해 정한 것과 같은 판단이다.
/// </summary>
public class RoutingFolderSourceTests
{
    private readonly RecordingSource folders = new("folder");
    private readonly RecordingSource shares = new("share");

    [Fact]
    public async Task Enumerate_ForALocalFolder_GoesToTheFileSystem()
    {
        var routed = await Names(Create().EnumerateAsync(Folder(@"C:\Temp"), CancellationToken.None));

        Assert.Equal(["folder"], routed);
        Assert.Empty(shares.Seen);
    }

    [Fact]
    public async Task Enumerate_ForAShareRoot_GoesToTheFileSystem()
    {
        // \\server\share 는 공유 목록이 아니라 그 공유 안이다. 라우팅이 여기서 갈리면
        // 네트워크 폴더가 전부 공유 목록으로 열린다.
        var routed = await Names(Create().EnumerateAsync(Folder(@"\\server\share"), CancellationToken.None));

        Assert.Equal(["folder"], routed);
    }

    [Fact]
    public async Task Enumerate_ForAServer_GoesToTheShareSource()
    {
        var routed = await Names(Create().EnumerateAsync(Folder(@"\\server"), CancellationToken.None));

        Assert.Equal(["share"], routed);
        Assert.Empty(folders.Seen);
    }

    [Fact]
    public async Task TryGetItem_ForAServer_GoesToTheShareSource()
    {
        await Create().TryGetItemAsync(Folder(@"\\server"), CancellationToken.None);

        Assert.Equal([@"\\server"], shares.Seen);
        Assert.Empty(folders.Seen);
    }

    [Fact]
    public async Task TryGetItem_ForAFile_GoesToTheFileSystem()
    {
        await Create().TryGetItemAsync(Folder(@"\\server\share\a.txt"), CancellationToken.None);

        Assert.Equal([@"\\server\share\a.txt"], folders.Seen);
    }

    private RoutingFolderSource Create() => new(folders, shares);

    private static async Task<string[]> Names(IAsyncEnumerable<FileItem> items)
    {
        var names = new List<string>();

        await foreach (var item in items)
        {
            names.Add(item.Name);
        }

        return [.. names];
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    /// <summary>어느 쪽으로 갔는지만 기록한다. 실물 열거는 각자의 테스트가 본다.</summary>
    private sealed class RecordingSource(string name) : IFolderSource
    {
        public List<string> Seen { get; } = [];

        public async IAsyncEnumerable<FileItem> EnumerateAsync(
            LocationId folder,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Seen.Add(folder.DisplayPath);
            await Task.Yield();

            yield return new FileItem(name, folder.Combine(name), 0, default, FileItemFlags.Directory);
        }

        public Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
        {
            Seen.Add(item.DisplayPath);
            return Task.FromResult<FileItem?>(null);
        }
    }
}
