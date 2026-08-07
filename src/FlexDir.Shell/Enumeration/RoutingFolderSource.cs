using FlexDir.Core.Enumeration;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Shell.Enumeration;

/// <summary>
/// 위치의 모양을 보고 열거를 넘긴다 — 서버(<c>\\server</c>)면 공유 목록, 그 밖에는
/// 파일시스템이다 (docs/PRD-v2.md §5 N-3).
/// <para>
/// <b>한 클래스에 두 기제를 섞지 않는 이유</b>: 공유 열거는 <c>WNetEnumResource</c> 이고
/// 폴더 열거는 <c>FileSystemEnumerator</c> 다. 취소·오류·페이징이 전부 다르므로 한 몸에
/// 두면 어느 쪽 규칙인지 읽을 수 없게 된다. ADR-014 가 PIDL 열거에 대해 "따로 세운다" 고
/// 정한 것과 같은 판단이고, 대신 <b>라우팅 규칙 자체가 여기서 채점된다.</b>
/// </para>
/// </summary>
public sealed class RoutingFolderSource(IFolderSource fileSystem, IFolderSource shares) : IFolderSource
{
    private readonly IFolderSource fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IFolderSource shares = shares ?? throw new ArgumentNullException(nameof(shares));

    public IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return Route(folder).EnumerateAsync(folder, ct);
    }

    public Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Route(item).TryGetItemAsync(item, ct);
    }

    // \\server 만 공유 목록이다. \\server\share 는 그 공유 '안' 이므로 파일시스템으로 간다 —
    // 여기서 갈리면 네트워크 폴더가 전부 공유 목록으로 열린다.
    private IFolderSource Route(LocationId location) => location.IsNetworkServer ? shares : fileSystem;
}
