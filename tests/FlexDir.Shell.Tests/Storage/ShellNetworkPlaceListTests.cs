using FlexDir.Shell.Storage;

using Xunit;

namespace FlexDir.Shell.Tests.Storage;

/// <summary>
/// '네트워크 위치 추가' 로 등록된 곳을 읽는 <see cref="ShellNetworkPlaceList"/>.
/// <para>
/// 바로가기 해석을 바꿔 끼워 <b>폴더를 훑고 라벨을 붙이는 규칙</b>만 채점한다 —
/// 실물 <c>IShellLink</c> 는 이 기계에 무엇이 등록돼 있느냐에 달렸다
/// (<c>ShellItemActivator</c> 가 실행 지점을 바꿔 끼우는 것과 같은 자리).
/// </para>
/// </summary>
public class ShellNetworkPlaceListTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"flexdir-places-{Guid.NewGuid():N}");

    public ShellNetworkPlaceListTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Windows 가 만드는 모양 그대로 — 이름 폴더 하나에 target.lnk 하나다.</summary>
    private void Register(string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "target.lnk"), string.Empty);
    }

    private ShellNetworkPlaceList Create(Func<string, string?> resolve)
        => new(root, resolve);

    [Fact]
    public async Task ListAsync_ReadsEveryRegisteredPlace()
    {
        Register("NAS_HOME");
        Register("DEV");

        using var places = Create(link => link.Contains("NAS_HOME", StringComparison.Ordinal)
            ? @"\\10.10.10.23\home"
            : @"\\10.10.20.30\rsj0811");

        var listed = await places.ListAsync(CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, place => place.Label == "NAS_HOME" && place.Path.DisplayPath == @"\\10.10.10.23\home");
    }

    [Fact]
    public async Task ListAsync_UsesTheFolderNameAsTheLabel()
    {
        // 사용자가 붙인 이름이다. 경로에서 유도하면 깊은 경로를 등록한 이유가 사라진다.
        Register("dev-server");

        using var places = Create(_ => @"\\10.10.20.30\rsj0811\workspace\docker-imx6-zeus\work");

        var place = Assert.Single(await places.ListAsync(CancellationToken.None));

        Assert.Equal("dev-server", place.Label);
        Assert.EndsWith(@"docker-imx6-zeus\work", place.Path.DisplayPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_SortsByLabelNaturally()
    {
        // 목록·트리와 한 규칙이다.
        Register("place10");
        Register("place2");
        Register("place1");

        using var places = Create(_ => @"\\server\share");

        var listed = await places.ListAsync(CancellationToken.None);

        Assert.Equal(["place1", "place2", "place10"], listed.Select(place => place.Label));
    }

    [Fact]
    public async Task ListAsync_SkipsAFolderWithoutALink()
    {
        // 사용자가 이 폴더 안에 뭔가 만들어 둘 수 있다. 링크가 없으면 갈 곳이 없다.
        Directory.CreateDirectory(Path.Combine(root, "그냥 폴더"));
        Register("NAS_HOME");

        using var places = Create(_ => @"\\10.10.10.23\home");

        Assert.Single(await places.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_SkipsWhatDoesNotResolve()
    {
        // 깨진 바로가기다. 트리에 올리면 눌러도 갈 곳이 없다.
        Register("깨진 것");

        using var places = Create(_ => null);

        Assert.Empty(await places.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_SkipsTargetsThatAreNotPaths()
    {
        // 대상이 경로가 아닐 수 있다 (shell 네임스페이스 항목). LocationId 가 거부하면 뺀다.
        Register("이상한 것");

        using var places = Create(_ => "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");

        Assert.Empty(await places.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WhenNothingIsRegistered_IsEmpty()
    {
        using var places = Create(_ => @"\\server\share");

        Assert.Empty(await places.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WhenTheFolderIsNotThere_IsEmptyNotAThrow()
    {
        // 한 번도 등록한 적 없는 기계에는 이 폴더가 아예 없다.
        using var places = new ShellNetworkPlaceList(Path.Combine(root, "없음"), _ => @"\\server\share");

        Assert.Empty(await places.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        using var places = Create(_ => @"\\server\share");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await places.ListAsync(cts.Token));
    }

    [Fact]
    public async Task ListAsync_OnThisMachine_ReadsRealShortcutsWithoutThrowing()
    {
        // 실물 IShellLink 경로를 한 번 지나가게 한다. 무엇이 등록돼 있는지는 기계마다
        // 다르므로 모양만 본다 — 나온 것은 전부 네트워크 경로여야 한다.
        using var places = new ShellNetworkPlaceList();

        foreach (var place in await places.ListAsync(CancellationToken.None))
        {
            Assert.True(place.Path.IsNetwork, $"네트워크 경로가 아니다: {place.Path.DisplayPath}");
            Assert.False(string.IsNullOrWhiteSpace(place.Label));
        }
    }
}
