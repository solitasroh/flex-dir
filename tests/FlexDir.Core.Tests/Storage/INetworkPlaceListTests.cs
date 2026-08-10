using FlexDir.Core.Locations;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Storage;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>ShellNetworkPlaceList</c>)는
/// <c>FlexDir.Shell</c> 의 몫이다.
/// <para>
/// <see cref="IDriveList"/> 와 나누는 이유: '네트워크 위치 추가' 로 등록한 곳은 드라이브
/// 문자를 만들지 않아 <c>WNetGetConnection</c> 에 잡히지 않는다. 읽는 기제가 아예 다르다
/// (바로가기 파일 + COM) — 한 클래스에 두 기제를 섞지 않는다는 규칙 그대로다.
/// </para>
/// </summary>
public class FakeNetworkPlaceListTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task ListAsync_GivesWhatWasPutIn()
    {
        var places = new FakeNetworkPlaceList();
        places.Places.Add(new NetworkPlace(Path(@"\\10.10.10.23\home"), "NAS_HOME"));

        var listed = await places.ListAsync(CancellationToken.None);

        Assert.Equal([new NetworkPlace(Path(@"\\10.10.10.23\home"), "NAS_HOME")], listed);
    }

    [Fact]
    public async Task ListAsync_WithNothingRegistered_IsEmptyNotNull()
    {
        Assert.Empty(await new FakeNetworkPlaceList().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_KeepsTheLabelApartFromThePath()
    {
        // 사용자가 붙인 이름이 라벨이다. 경로에서 유도하면 깊은 경로를 등록한 이유가 사라진다
        // (\\서버\공유\a\b\c 를 'dev-server' 로 등록하는 것이 그 기능의 요점이다).
        var places = new FakeNetworkPlaceList();
        places.Places.Add(new NetworkPlace(Path(@"\\10.10.20.30\rsj0811\workspace\work"), "dev-server"));

        var place = Assert.Single(await places.ListAsync(CancellationToken.None));

        Assert.Equal("dev-server", place.Label);
        Assert.EndsWith(@"workspace\work", place.Path.DisplayPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeNetworkPlaceList().ListAsync(cts.Token));
    }
}
