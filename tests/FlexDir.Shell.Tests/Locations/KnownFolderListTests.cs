using FlexDir.Core.Locations;
using FlexDir.Shell.Locations;

using Xunit;

namespace FlexDir.Shell.Tests.Locations;

/// <summary>
/// 실물 알려진 폴더를 읽는 <see cref="KnownFolderList"/>.
/// <para>
/// 어느 폴더가 어디 있는지는 기계마다 다르므로 여기서 보는 것은 <b>모양</b>이다
/// (<see cref="Storage.SystemDriveListTests"/> 와 같은 구도): 다섯이 항상 다섯으로,
/// 선언 순서 그대로, 고정 라벨을 달고 온다. 값 자체는
/// <see cref="RegistrySystemThemeSourceTests"/> 처럼 <b>같은 원천을 직접 다시 읽어</b>
/// 비교한다.
/// </para>
/// </summary>
public class KnownFolderListTests
{
    private static readonly string[] Labels = ["홈", "바탕화면", "문서", "다운로드", "사진"];

    [Fact]
    public async Task ListAsync_AlwaysReturnsAllFiveKindsInDeclarationOrder()
    {
        var list = new KnownFolderList();

        var folders = await list.ListAsync(CancellationToken.None);

        // 없는 폴더도 Location = null 로 자리를 지킨다 — 거르는 것은 ViewModel 의 일이다.
        Assert.Equal(
            [KnownFolderKind.Home, KnownFolderKind.Desktop, KnownFolderKind.Documents, KnownFolderKind.Downloads, KnownFolderKind.Pictures],
            folders.Select(folder => folder.Kind));
    }

    [Fact]
    public async Task ListAsync_LabelsAreTheFixedKoreanFive()
    {
        // OS 언어가 무엇이든 라벨은 구현체가 정한 다섯이다 — 경로 조각을 라벨로 쓰면
        // 영문 Windows 에서 메뉴에 영문과 한글이 섞인다.
        var list = new KnownFolderList();

        var folders = await list.ListAsync(CancellationToken.None);

        Assert.Equal(Labels, folders.Select(folder => folder.Label));
    }

    [Fact]
    public async Task ListAsync_HomeAlwaysHasALocation()
    {
        // 사용자 프로필 없이는 프로세스가 돌지 않으므로 이것 하나는 기계와 무관하게 참이다.
        var list = new KnownFolderList();

        var folders = await list.ListAsync(CancellationToken.None);

        Assert.NotNull(folders.Single(folder => folder.Kind == KnownFolderKind.Home).Location);
    }

    [Fact]
    public async Task ListAsync_EveryLocationIsAnAbsoluteParsedPath()
    {
        var list = new KnownFolderList();

        foreach (var folder in await list.ListAsync(CancellationToken.None))
        {
            if (folder.Location is not { } location)
            {
                continue;
            }

            // LocationId 를 지나 나온 값이면 표시형이 다시 파싱되어 자기 자신과 같다.
            Assert.True(Path.IsPathFullyQualified(location.DisplayPath), $"절대 경로가 아니다: {location.DisplayPath}");
            Assert.True(LocationId.TryParse(location.DisplayPath, out var reparsed, out _), $"되파싱이 안 된다: {location.DisplayPath}");
            Assert.Equal(location, reparsed);
        }
    }

    [Theory]
    [InlineData(KnownFolderKind.Home, Environment.SpecialFolder.UserProfile)]
    [InlineData(KnownFolderKind.Desktop, Environment.SpecialFolder.DesktopDirectory)]
    [InlineData(KnownFolderKind.Documents, Environment.SpecialFolder.MyDocuments)]
    [InlineData(KnownFolderKind.Pictures, Environment.SpecialFolder.MyPictures)]
    public async Task ListAsync_MatchesTheSpecialFolderReadDirectly(
        KnownFolderKind kind, Environment.SpecialFolder special)
    {
        // 이 기계에 그 폴더가 있는지는 세션마다 다르므로, 직접 다시 읽은 값과 같은지만 본다
        // — RegistrySystemThemeSourceTests 와 같은 구도다. Downloads 는
        // Environment.SpecialFolder 에 없어 여기 못 들어간다.
        var list = new KnownFolderList();

        var folders = await list.ListAsync(CancellationToken.None);
        var location = folders.Single(folder => folder.Kind == kind).Location;

        var raw = Environment.GetFolderPath(special);

        if (raw.Length == 0 || !LocationId.TryParse(raw, out var expected, out _))
        {
            Assert.Null(location);
        }
        else
        {
            Assert.Equal(expected, location);
        }
    }

    [Fact]
    public async Task ListAsync_CalledTwice_ReturnsTheSameFive()
    {
        var list = new KnownFolderList();

        var first = await list.ListAsync(CancellationToken.None);
        var second = await list.ListAsync(CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ListAsync_DoesNotCompleteSynchronously()
    {
        // Task.Run 을 거치는지 직접 볼 길이 없어 대신 동기 완료가 아님을 본다 — 동기 완료면
        // 리디렉션된 폴더(OneDrive · 도메인 로밍)의 네트워크 블로킹이 호출 스레드를 잡는다.
        var list = new KnownFolderList();

        var pending = list.ListAsync(CancellationToken.None);
        var completedOnReturn = pending.IsCompleted;

        await pending;

        Assert.False(completedOnReturn);
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        var list = new KnownFolderList();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await list.ListAsync(cts.Token));
    }
}
