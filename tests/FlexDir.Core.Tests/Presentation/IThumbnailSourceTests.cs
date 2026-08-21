using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Presentation;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// <c>FlexDir.Shell</c> 의 구현체도 같은 검증을 받는다.
/// <para>
/// <see cref="ThumbnailBitmap"/> 의 길이 검증을 여기서 못 박는다: BGRA32 는 Stride 가
/// <c>Width * 4</c> 이므로 길이가 어긋난 버퍼는 <c>WriteableBitmap</c> 에 넣는 순간
/// 엉뚱하게 그려지거나 터진다. 만드는 자리에서 걸러야 원인이 남는다.
/// </para>
/// <para>
/// 나머지 절반은 <b>세는 쪽을 먼저 믿을 수 있게</b> 하는 것이다. "확장자당 한 번만 조회"
/// 와 "동시 요청 4개" 는 호출자(<c>ThumbnailRequestScheduler</c>)가 지키는 규칙이고,
/// 그 검증은 이 fake 의 기록·<see cref="FakeThumbnailSource.PeakConcurrency"/> 에 의존한다.
/// </para>
/// </summary>
public class FakeThumbnailSourceTests
{
    private const int IconSize = 16;

    // ── ThumbnailBitmap ───────────────────────────────────────────

    [Fact]
    public void ThumbnailBitmap_TakesABgra32Buffer()
    {
        var bitmap = new ThumbnailBitmap(2, 3, new byte[2 * 3 * 4]);

        Assert.Equal(2, bitmap.Width);
        Assert.Equal(3, bitmap.Height);
        Assert.Equal(24, bitmap.Pixels.Length);
    }

    [Theory]
    [InlineData(2, 2, 15)]
    [InlineData(2, 2, 17)]
    [InlineData(2, 2, 0)]
    [InlineData(96, 96, 96 * 96)]
    public void ThumbnailBitmap_PixelLengthThatDoesNotMatchTheSize_Throws(int width, int height, int length)
    {
        Assert.Throws<ArgumentException>(() => new ThumbnailBitmap(width, height, new byte[length]));
    }

    [Fact]
    public void ThumbnailBitmap_NullPixels_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ThumbnailBitmap(2, 2, null!));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-1, 2)]
    public void ThumbnailBitmap_NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThumbnailBitmap(width, height, new byte[16]));
    }

    // ── fake 의 계약 ──────────────────────────────────────────────

    [Fact]
    public async Task GetTypeIconAsync_ReturnsAnIconOfTheRequestedSize()
    {
        var source = new FakeThumbnailSource();

        var icon = await source.GetTypeIconAsync("txt", isDirectory: false, IconSize, CancellationToken.None);

        Assert.NotNull(icon);
        Assert.Equal(IconSize, icon.Width);
        Assert.Equal(FakeThumbnailSource.IconMark, icon.Pixels[0]);
    }

    [Fact]
    public async Task GetThumbnailAsync_ReturnsSomethingDistinguishableFromATypeIcon()
    {
        var source = new FakeThumbnailSource();

        var thumbnail = await source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(96, thumbnail.Width);

        // 호출자가 형식 아이콘을 썸네일 자리에 넣어도 테스트가 눈치채지 못하면 안 된다.
        Assert.Equal(FakeThumbnailSource.ThumbnailMark, thumbnail.Pixels[0]);
    }

    [Fact]
    public async Task Requests_RecordEveryInvocation_SoRepeatedLookupsAreVisible()
    {
        var source = new FakeThumbnailSource();
        var item = Loc(@"C:\Temp\a.jpg");

        await source.GetTypeIconAsync("jpg", isDirectory: false, IconSize, CancellationToken.None);
        await source.GetTypeIconAsync("jpg", isDirectory: false, IconSize, CancellationToken.None);
        await source.GetThumbnailAsync(item, 96, CancellationToken.None);

        // fake 가 캐시해버리면 호출자의 중복 조회가 보이지 않는다.
        Assert.Equal(2, source.CountTypeIconRequests("jpg", isDirectory: false));
        Assert.Equal(1, source.CountThumbnailRequests(item));
    }

    [Fact]
    public async Task CountTypeIconRequests_SeparatesDirectoriesFromExtensionlessFiles()
    {
        var source = new FakeThumbnailSource();

        await source.GetTypeIconAsync(string.Empty, isDirectory: true, IconSize, CancellationToken.None);
        await source.GetTypeIconAsync(string.Empty, isDirectory: false, IconSize, CancellationToken.None);

        // 디렉터리도 확장자가 빈 문자열로 온다. isDirectory 로 갈리지 않으면 셀 수 없다.
        Assert.Equal(1, source.CountTypeIconRequests(string.Empty, isDirectory: true));
        Assert.Equal(1, source.CountTypeIconRequests(string.Empty, isDirectory: false));
    }

    [Fact]
    public async Task MissingThumbnails_YieldNull_NotAnException()
    {
        var source = new FakeThumbnailSource();
        source.MissingThumbnails.Add("a.jpg");

        // 포트 계약이다: 없거나 실패하면 null 이다.
        Assert.Null(await source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, CancellationToken.None));
    }

    [Fact]
    public async Task ThumbnailFailure_IsThrownToTheCaller()
    {
        var source = new FakeThumbnailSource { ThumbnailFailure = new InvalidOperationException("shell") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, CancellationToken.None));
    }

    [Fact]
    public async Task TypeIconFailure_IsThrownToTheCaller()
    {
        var source = new FakeThumbnailSource { TypeIconFailure = new InvalidOperationException("shell") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.GetTypeIconAsync("txt", isDirectory: false, IconSize, CancellationToken.None));
    }

    [Fact]
    public async Task PeakConcurrency_RecordsTheHighestNumberOfOverlappingRequests()
    {
        var source = new FakeThumbnailSource();
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;

        var pending = new[]
        {
            source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, CancellationToken.None).AsTask(),
            source.GetThumbnailAsync(Loc(@"C:\Temp\b.jpg"), 96, CancellationToken.None).AsTask(),
            source.GetThumbnailAsync(Loc(@"C:\Temp\c.jpg"), 96, CancellationToken.None).AsTask(),
        };

        Assert.Equal(3, source.PeakConcurrency);

        gate.SetResult();
        await Task.WhenAll(pending);

        // 지나간 최대치를 기억한다. 끝난 뒤에 0 으로 돌아가면 잴 수 없다.
        Assert.Equal(3, source.PeakConcurrency);
    }

    [Fact]
    public async Task GetThumbnailAsync_WhenAlreadyCanceled_ObservesCancellation()
    {
        var source = new FakeThumbnailSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, cts.Token));

        Assert.Equal(1, source.CancellationsObserved);
    }

    [Fact]
    public async Task GetThumbnailAsync_CancelWhileWaitingAtTheGate_ObservesCancellation()
    {
        var source = new FakeThumbnailSource();
        var gate = new TaskCompletionSource();
        source.ThumbnailGate = gate.Task;

        using var cts = new CancellationTokenSource();
        var pending = source.GetThumbnailAsync(Loc(@"C:\Temp\a.jpg"), 96, cts.Token).AsTask();

        // 관문이 열리기 전에 취소된다. 스크롤 중 취소가 정확히 이 모양이다.
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(1, source.CancellationsObserved);
    }

    [Fact]
    public async Task IsUsableThroughThePortAlone()
    {
        // 호출부는 fake 를 모른다. 포트만으로 쓸 수 있어야 한다.
        IThumbnailSource port = new FakeThumbnailSource();

        Assert.NotNull(await port.GetTypeIconAsync("png", isDirectory: false, IconSize, CancellationToken.None));
        Assert.NotNull(await port.GetThumbnailAsync(Loc(@"C:\Temp\a.png"), 96, CancellationToken.None));
    }

    // ── GetItemIconAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetItemIconAsync_ReturnsTheIconInjectedForThatPath()
    {
        var source = new FakeThumbnailSource();
        var item = Loc(@"C:\Users\x\Downloads");
        source.ItemIcons[item] = FakeThumbnailSource.Bitmap(IconSize, FakeThumbnailSource.ItemIconMark);

        var icon = await source.GetItemIconAsync(item, IconSize, CancellationToken.None);

        Assert.NotNull(icon);
        Assert.Equal(IconSize, icon.Width);

        // 형식 아이콘·썸네일과 갈려야 호출자가 엉뚱한 그림을 꽂아도 테스트가 눈치챈다.
        Assert.Equal(FakeThumbnailSource.ItemIconMark, icon.Pixels[0]);
    }

    [Fact]
    public async Task GetItemIconAsync_WithoutAnInjectedIcon_YieldsNull_NotAnException()
    {
        var source = new FakeThumbnailSource();

        // 포트 계약이다: 없거나 실패하면 null 이다.
        Assert.Null(await source.GetItemIconAsync(Loc(@"C:\Temp\없는곳"), IconSize, CancellationToken.None));
    }

    [Fact]
    public async Task GetItemIconAsync_WhenAlreadyCanceled_ObservesCancellation()
    {
        var source = new FakeThumbnailSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.GetItemIconAsync(Loc(@"C:\Users\x\Downloads"), IconSize, cts.Token));

        Assert.Equal(1, source.CancellationsObserved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetItemIconAsync_NonPositiveSize_Throws(int size)
    {
        var source = new FakeThumbnailSource();

        // 구현체(ShellThumbnailSource)가 같은 검증을 던진다. fake 만 관대하면
        // 테스트는 초록인데 실물에서 터지는 자리가 된다.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await source.GetItemIconAsync(Loc(@"C:\Users\x\Downloads"), size, CancellationToken.None));
    }

    [Fact]
    public async Task CountItemIconRequests_RecordsEveryInvocationPerPath()
    {
        var source = new FakeThumbnailSource();
        var downloads = Loc(@"C:\Users\x\Downloads");
        var pictures = Loc(@"C:\Users\x\Pictures");

        await source.GetItemIconAsync(downloads, IconSize, CancellationToken.None);
        await source.GetItemIconAsync(downloads, IconSize, CancellationToken.None);
        await source.GetItemIconAsync(pictures, IconSize, CancellationToken.None);

        // 경로마다 캐시가 잘 듣는지("한 번만 묻는가")는 호출자의 규칙이고, 그 채점은
        // 이 기록에 의존한다. fake 가 캐시해버리면 중복 조회가 보이지 않는다.
        Assert.Equal(2, source.CountItemIconRequests(downloads));
        Assert.Equal(1, source.CountItemIconRequests(pictures));
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
