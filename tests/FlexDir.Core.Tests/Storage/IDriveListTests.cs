using FlexDir.Core.Locations;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Storage;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>SystemDriveList</c>)는
/// <c>FlexDir.Shell</c> 의 몫이다 — <see cref="IDriveSpace"/> 와 같은 구도다.
/// <para>
/// 이 포트가 따로 있는 이유: 드라이브 열거는 <b>저장소에 닿는다</b>.
/// <c>DriveInfo.GetDrives</c> 는 연결이 끊긴 매핑 드라이브에서 초 단위로 블로킹하므로
/// 트리 ViewModel 이 직접 부르면 UI 스레드가 거기서 멈춘다 (CLAUDE.md §3).
/// </para>
/// </summary>
public class FakeDriveListTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task ListAsync_GivesWhatWasPutIn()
    {
        var drives = new FakeDriveList();
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));

        var listed = await drives.ListAsync(CancellationToken.None);

        Assert.Equal([new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null)], listed);
    }

    [Fact]
    public async Task ListAsync_WithNoDrives_IsEmptyNotNull()
    {
        // 드라이브가 하나도 없는 것은 오류가 아니다 — 트리가 빈 루트를 그리면 된다.
        var listed = await new FakeDriveList().ListAsync(CancellationToken.None);

        Assert.Empty(listed);
    }

    [Fact]
    public async Task ListAsync_ForAMappedDrive_CarriesTheServer()
    {
        // 트리의 네트워크 항목은 매핑 드라이브에서 뽑는다 (사용자 결정 2026-08-10) —
        // 서버를 여기 싣지 않으면 트리가 UNC 를 알 길이 없다.
        var drives = new FakeDriveList();
        drives.Drives.Add(new DriveEntry(Path(@"Z:\"), "nas (Z:)", Path(@"\\10.10.10.23")));

        var mapped = Assert.Single(await drives.ListAsync(CancellationToken.None));

        Assert.NotNull(mapped.Server);
        Assert.True(mapped.Server.IsNetworkServer);
    }

    [Fact]
    public async Task ListAsync_ForALocalDrive_HasNoServer()
    {
        var drives = new FakeDriveList();
        drives.Drives.Add(new DriveEntry(Path(@"C:\"), "로컬 디스크 (C:)", null));

        Assert.Null(Assert.Single(await drives.ListAsync(CancellationToken.None)).Server);
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        // 취소는 삼키지 않는다 — 창을 닫는 중에 열거가 남으면 안 된다.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeDriveList().ListAsync(cts.Token));
    }
}
