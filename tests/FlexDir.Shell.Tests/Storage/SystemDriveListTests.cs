using FlexDir.Shell.Storage;

using Xunit;

namespace FlexDir.Shell.Tests.Storage;

/// <summary>
/// 실물 드라이브를 세는 <see cref="SystemDriveList"/>.
/// <para>
/// 어떤 드라이브가 붙어 있는지는 기계마다 다르므로 여기서 보는 것은 <b>모양</b>이다:
/// 시스템 드라이브는 반드시 있고, 항목마다 라벨과 파싱되는 루트가 있으며, 서버는
/// 네트워크 드라이브에만 붙는다.
/// </para>
/// </summary>
public class SystemDriveListTests
{
    [Fact]
    public async Task ListAsync_IncludesTheDriveWeAreRunningOn()
    {
        var drives = new SystemDriveList();

        var listed = await drives.ListAsync(CancellationToken.None);
        var here = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Contains(listed, drive => string.Equals(drive.Root.DisplayPath, here, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_EveryEntryCarriesALabelWithItsLetter()
    {
        // 라벨이 비면 트리에 빈 줄이 선다. 볼륨 이름이 없는 드라이브도 유형 이름으로 선다.
        var drives = new SystemDriveList();

        foreach (var drive in await drives.ListAsync(CancellationToken.None))
        {
            Assert.False(string.IsNullOrWhiteSpace(drive.Label), $"라벨이 비었다: {drive.Root.DisplayPath}");

            // "로컬 디스크 (C:)" — 문자가 없으면 같은 이름의 볼륨 둘을 구분할 수 없다.
            var letter = drive.Root.DisplayPath.TrimEnd('\\');
            Assert.Contains($"({letter})", drive.Label, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ListAsync_ServersComeOnlyFromMappedDrives()
    {
        // 매핑 드라이브에는 서버가 반드시 붙고 나머지에는 붙지 않는다. 매핑이 없는
        // 기계에서는 앞의 절반이 조용히 지나간다 — 실물 테스트가 치르는 값이다.
        var drives = new SystemDriveList();
        var mapped = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Network)
            .Select(drive => drive.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in await drives.ListAsync(CancellationToken.None))
        {
            if (!mapped.Contains(drive.Root.DisplayPath))
            {
                // C: 에 서버가 붙으면 트리가 로컬 디스크를 네트워크 항목으로 낸다.
                Assert.Null(drive.Server);
                continue;
            }

            Assert.NotNull(drive.Server);

            // 공유가 아니라 서버까지다 — 그 아래 공유 목록은 라우팅이 따로 낸다.
            Assert.True(drive.Server.IsNetworkServer, $"서버가 서버 형태가 아니다: {drive.Server.DisplayPath}");
        }
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        var drives = new SystemDriveList();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await drives.ListAsync(cts.Token));
    }
}
