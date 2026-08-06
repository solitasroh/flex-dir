using FlexDir.Core.Locations;
using FlexDir.Core.Storage;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Storage;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이고
/// 실제 구현체(<c>ShellDriveSpace</c>)는 <c>FlexDir.Shell</c> 의 몫이다.
/// <para>
/// 이 포트가 따로 있는 이유: 여유 용량 조회는 <b>저장소에 닿는다</b>. 네트워크·클라우드
/// 경로에서는 초 단위로 블로킹하므로 (CLAUDE.md §3) ViewModel 이 직접 <c>DriveInfo</c> 를
/// 부르면 UI 스레드가 거기서 멈춘다. 그리고 포트로 갈라야 ViewModel 테스트가 실제 드라이브
/// 없이 돈다.
/// </para>
/// </summary>
public class FakeDriveSpaceTests
{
    private static LocationId Folder(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task MeasureAsync_GivesWhatWasPutIn()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var space = new FakeDriveSpace();
        space.Spaces[folder] = new DriveSpace(FreeBytes: 213_000_000_000, TotalBytes: 512_000_000_000);

        var measured = await space.MeasureAsync(folder, CancellationToken.None);

        Assert.Equal(new DriveSpace(213_000_000_000, 512_000_000_000), measured);
    }

    [Fact]
    public async Task MeasureAsync_ForAnUnknownLocation_IsNull()
    {
        // 답을 모르는 것과 0 바이트가 남은 것은 다르다. 상태표시줄이 "여유 공간 0 B" 를
        // 띄우면 디스크가 꽉 찼다는 뜻이 되므로 모를 때는 아예 적지 않는다.
        var space = new FakeDriveSpace();

        Assert.Null(await space.MeasureAsync(Folder(@"C:\Temp"), CancellationToken.None));
    }

    [Fact]
    public async Task MeasureAsync_Cancelled_Throws()
    {
        // 취소는 삼키지 않는다 — 폴더를 빠르게 옮기면 이전 조회가 남는다.
        var space = new FakeDriveSpace();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await space.MeasureAsync(Folder(@"C:\Temp"), cts.Token));
    }
}
