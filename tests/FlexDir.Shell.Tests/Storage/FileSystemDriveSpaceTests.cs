using FlexDir.Core.Locations;
using FlexDir.Shell.Storage;

using Xunit;

namespace FlexDir.Shell.Tests.Storage;

/// <summary>
/// 실물 볼륨을 재는 <see cref="FileSystemDriveSpace"/>.
/// <para>
/// 절대값을 단정할 수 없다 — 기계마다 다르고 실행 중에도 바뀐다. 그래서 여기서 보는 것은
/// <b>모양</b>이다: 있는 드라이브는 답이 오고 0 &lt; 여유 ≤ 전체이며, 없는 드라이브는
/// 예외가 아니라 <c>null</c> 이다.
/// </para>
/// </summary>
public class FileSystemDriveSpaceTests
{
    private static LocationId Folder(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task MeasureAsync_ForARealFolder_GivesAPlausibleVolume()
    {
        var space = new FileSystemDriveSpace();

        var measured = await space.MeasureAsync(Folder(Path.GetTempPath().TrimEnd('\\')), CancellationToken.None);

        Assert.NotNull(measured);
        Assert.True(measured.Value.TotalBytes > 0, "전체 용량이 0 이면 볼륨을 못 읽은 것이다.");
        Assert.InRange(measured.Value.FreeBytes, 0, measured.Value.TotalBytes);
    }

    [Fact]
    public async Task MeasureAsync_ForAFolderDeepInsideTheVolume_AnswersForTheVolume()
    {
        // 폴더가 아니라 그 폴더가 올라앉은 볼륨을 물어야 한다. 깊은 경로에서도 같은 답이다.
        var space = new FileSystemDriveSpace();
        // 백슬래시를 떼면 안 된다 — "C:" 는 드라이브 상대 경로이고 LocationId 가 거부한다.
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        var deep = await space.MeasureAsync(Folder(Path.GetTempPath().TrimEnd('\\')), CancellationToken.None);
        var shallow = await space.MeasureAsync(Folder(root), CancellationToken.None);

        Assert.NotNull(deep);
        Assert.NotNull(shallow);
        Assert.Equal(shallow.Value.TotalBytes, deep.Value.TotalBytes);
    }

    [Fact]
    public async Task MeasureAsync_ForADriveThatIsNotThere_IsNullNotAThrow()
    {
        // 여유 용량은 곁다리 정보다. 못 읽는다고 폴더를 못 여는 것은 아니다.
        var space = new FileSystemDriveSpace();

        Assert.Null(await space.MeasureAsync(Folder(@"Q:\nowhere"), CancellationToken.None));
    }

    [Fact]
    public async Task MeasureAsync_Cancelled_Throws()
    {
        var space = new FileSystemDriveSpace();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await space.MeasureAsync(Folder(@"C:\"), cts.Token));
    }
}
