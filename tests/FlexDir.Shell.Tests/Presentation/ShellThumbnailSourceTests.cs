using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;
using FlexDir.Shell.Presentation;

using Xunit;

namespace FlexDir.Shell.Tests.Presentation;

/// <summary>
/// Windows Shell 에서 형식 아이콘과 썸네일을 얻는 구현체.
/// <para>
/// <b>픽셀 내용은 검증하지 않는다.</b> 아이콘 그림은 Windows 버전·테마·설치된 프로그램에
/// 따라 달라지므로 기계마다 다른 값을 게이트에 넣을 수 없다. 대신 <b>성질</b>을 잰다 —
/// 버퍼 길이가 <c>W*H*4</c> 인가, 확장자마다 갈리는가, 큰 크기를 물으면 더 큰 그림이
/// 오는가, 썸네일이 없는 항목은 <c>null</c> 인가, 그리고 <b>STA 에서 도는가</b>.
/// </para>
/// <para>
/// 자동으로 재지 못하는 것 둘은 <c>.harness/manual-plan.md</c> 에 사람 확인 항목으로 있다 —
/// GDI 핸들 누수(오래 스크롤하며 관찰해야 한다)와 클라우드 자리표시자(OneDrive 가 필요하다).
/// </para>
/// </summary>
public sealed class ShellThumbnailSourceTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "flex-dir-tests", Guid.NewGuid().ToString("N"));

    public ShellThumbnailSourceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 썸네일 추출기가 파일을 아직 쥐고 있을 수 있다. 임시 폴더라 남아도 무해하다.
        }
    }

    // ── 아파트먼트 ──────────────────────────────────────────────────
    // SHGetFileInfo·IShellItemImageFactory 는 STA 를 요구하고 스레드풀은 MTA 다
    // (docs/SHELL_NOTES.md §COM 아파트먼트). ShellTypeNameProvider 는 이 검증이 없는 동안
    // Task.Run 위에서 60개의 초록 테스트를 통과했다.

    [Fact]
    public async Task TypeIcon_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var source = new ShellThumbnailSource(
            (_, _, _) =>
            {
                apartment = Thread.CurrentThread.GetApartmentState();

                return null;
            },
            (_, _) => null,
            (_, _) => null);

        await source.GetTypeIconAsync("txt", false, 16, CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task Thumbnail_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var source = new ShellThumbnailSource(
            (_, _, _) => null,
            (_, _) =>
            {
                apartment = Thread.CurrentThread.GetApartmentState();

                return null;
            },
            (_, _) => null);

        await source.GetThumbnailAsync(Location("a.txt"), 96, CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    // GetItemIconAsync 도 SHGetFileInfo 위에 선다 — 다른 둘과 같이 STA 워커를 거쳐야 한다.
    [Fact]
    public async Task ItemIcon_PassesItemAndSizeToTheLookup_OnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;
        LocationId? seenItem = null;
        var seenSize = 0;

        using var source = new ShellThumbnailSource(
            (_, _, _) => null,
            (_, _) => null,
            (item, size) =>
            {
                apartment = Thread.CurrentThread.GetApartmentState();
                seenItem = item;
                seenSize = size;

                return null;
            });

        var location = Location("폴더");

        await source.GetItemIconAsync(location, 48, CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.Equal(location, seenItem);
        Assert.Equal(48, seenSize);
    }

    // ── 형식 아이콘 (실제 shell 에 물어본다) ────────────────────────

    [Fact]
    public async Task TypeIcon_ForKnownExtension_HasPixels()
    {
        using var source = new ShellThumbnailSource();

        var icon = await source.GetTypeIconAsync("txt", false, 16, CancellationToken.None);

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);

        // record 가 이미 검사하지만, 변환층이 stride 를 잘못 잡으면 여기서 먼저 드러난다.
        Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
    }

    [Fact]
    public async Task TypeIcon_DiffersByExtension()
    {
        using var source = new ShellThumbnailSource();

        var text = await source.GetTypeIconAsync("txt", false, 32, CancellationToken.None);
        var executable = await source.GetTypeIconAsync("exe", false, 32, CancellationToken.None);

        Assert.NotNull(text);
        Assert.NotNull(executable);
        Assert.False(text.Pixels.AsSpan().SequenceEqual(executable.Pixels));
    }

    // 디렉터리도 확장자가 빈 문자열로 온다. isDirectory 로 갈라야 폴더 아이콘과 확장자 없는
    // 파일의 아이콘이 구분된다.
    [Fact]
    public async Task TypeIcon_DirectoryAndFile_Differ()
    {
        using var source = new ShellThumbnailSource();

        var folder = await source.GetTypeIconAsync(string.Empty, true, 32, CancellationToken.None);
        var file = await source.GetTypeIconAsync(string.Empty, false, 32, CancellationToken.None);

        Assert.NotNull(folder);
        Assert.NotNull(file);
        Assert.False(folder.Pixels.AsSpan().SequenceEqual(file.Pixels));
    }

    // 큰 아이콘 뷰는 96 을 쓴다 (docs/DESIGN.md §2). SHGFI_ICON 은 16·32 밖에 못 내므로
    // 시스템 이미지 리스트를 골라 써야 이 성질이 성립한다 — 이 테스트가 그 경로를 고정한다.
    [Fact]
    public async Task TypeIcon_LargerRequest_YieldsALargerImage()
    {
        using var source = new ShellThumbnailSource();

        var small = await source.GetTypeIconAsync("txt", false, 16, CancellationToken.None);
        var large = await source.GetTypeIconAsync("txt", false, 96, CancellationToken.None);

        Assert.NotNull(small);
        Assert.NotNull(large);
        Assert.True(large.Width > small.Width, $"{large.Width} > {small.Width}");
    }

    // 유형 컬럼과 스케줄러의 아이콘 캐시가 확장자를 키로 쓴다. .JPG 와 .jpg 가 갈리면
    // 같은 그림을 두 번 사 온다.
    [Fact]
    public async Task TypeIcon_ExtensionCase_DoesNotMatter()
    {
        using var source = new ShellThumbnailSource();

        var lower = await source.GetTypeIconAsync("txt", false, 32, CancellationToken.None);
        var upper = await source.GetTypeIconAsync("TXT", false, 32, CancellationToken.None);

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.True(lower.Pixels.AsSpan().SequenceEqual(upper.Pixels));
    }

    // 등록되지 않은 확장자도 Windows 가 일반 파일 아이콘을 준다. null 이면 목록에 빈칸이
    // 생기고, 자리를 비워두지 않는 것이 규칙이다 (docs/UI_GUIDE.md §상태 표현).
    [Fact]
    public async Task TypeIcon_UnregisteredExtension_StillHasPixels()
    {
        using var source = new ShellThumbnailSource();

        Assert.NotNull(await source.GetTypeIconAsync("zzzzz", false, 16, CancellationToken.None));
    }

    // 파일을 건드리지 않는다 — 센티널 이름 + SHGFI_USEFILEATTRIBUTES (SHELL_NOTES §아이콘).
    // 존재하지 않는 확장자로도 아이콘이 나오는 것이 그 증거다.
    [Fact]
    public async Task TypeIcon_NeedsNoFileToExist()
    {
        using var source = new ShellThumbnailSource();

        Assert.NotNull(await source.GetTypeIconAsync("존재하지않는확장자", false, 16, CancellationToken.None));
    }

    // ── 항목 아이콘 (실제 shell 에 물어본다) ────────────────────────

    [Fact]
    public async Task ItemIcon_ForARealFolder_HasPixels()
    {
        var profile = Parse(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        using var source = new ShellThumbnailSource();

        var icon = await source.GetItemIconAsync(profile, 32, CancellationToken.None);

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
        Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
    }

    // <b>이 step 의 진짜 판정이다.</b> SHGFI_USEFILEATTRIBUTES 를 남기면 shell 이 실제
    // 항목을 보지 않아 모든 폴더가 같은 아이콘이 되고, 이 테스트만 그것을 잡는다.
    // 픽셀 값은 단정하지 않는다 — 테마·Windows 버전·DPI 에 따라 그림이 다르다.
    // 두 폴더 중 하나라도 없는 기계에서는 단정을 건너뛴다 (Skip 이 아니다 — 게이트를 막지 않는다).
    [Fact]
    public async Task ItemIcon_KnownFolders_Differ()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        if (!Directory.Exists(downloads) || !Directory.Exists(pictures))
        {
            return;
        }

        using var source = new ShellThumbnailSource();

        var first = await source.GetItemIconAsync(Parse(downloads), 32, CancellationToken.None);
        var second = await source.GetItemIconAsync(Parse(pictures), 32, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.False(first.Pixels.AsSpan().SequenceEqual(second.Pixels));
    }

    [Fact]
    public async Task ItemIcon_LookupYieldsNull_IsNull()
    {
        using var source = new ShellThumbnailSource(
            (_, _, _) => null,
            (_, _) => null,
            (_, _) => null);

        Assert.Null(await source.GetItemIconAsync(Location("폴더"), 32, CancellationToken.None));
    }

    // ── 썸네일 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Thumbnail_ForBitmapFile_HasPixels()
    {
        var file = WriteBitmap("그림.bmp", 32);

        using var source = new ShellThumbnailSource();

        var thumbnail = await source.GetThumbnailAsync(file, 96, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(thumbnail.Width * thumbnail.Height * 4, thumbnail.Pixels.Length);
    }

    // GetImage 는 기본이 SIIGBF_RESIZETOFIT 이다. 요청보다 큰 그림이 오면 스케줄러의
    // 크기별 캐시 키가 뜻을 잃고 BGRA 버퍼도 예상보다 커진다.
    [Fact]
    public async Task Thumbnail_IsNotLargerThanRequested()
    {
        var file = WriteBitmap("큰그림.bmp", 256);

        using var source = new ShellThumbnailSource();

        var thumbnail = await source.GetThumbnailAsync(file, 96, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.True(thumbnail.Width <= 96, $"{thumbnail.Width} <= 96");
        Assert.True(thumbnail.Height <= 96, $"{thumbnail.Height} <= 96");
    }

    [Fact]
    public async Task Thumbnail_ForMissingFile_IsNull()
    {
        using var source = new ShellThumbnailSource();

        Assert.Null(await source.GetThumbnailAsync(Location("없는파일.bmp"), 96, CancellationToken.None));
    }

    // <b>SIIGBF_THUMBNAILONLY 를 고정하는 테스트다.</b> 그것을 주지 않으면 썸네일이 없을 때
    // shell 이 아이콘을 대신 돌려주고, 그러면 "실제 썸네일. 없으면 null" 계약이 깨진다.
    // 계약이 깨지면 호출자의 "실패는 재시도하지 않는다"(docs/PRD.md §4)도 뜻을 잃는다.
    [Fact]
    public async Task Thumbnail_ForItemWithoutAHandler_IsNull()
    {
        var file = Path.Combine(root, "손잡이없음.flexdirtest");
        File.WriteAllText(file, "썸네일 처리기가 등록될 리 없는 확장자다.");

        using var source = new ShellThumbnailSource();

        Assert.Null(await source.GetThumbnailAsync(Location("손잡이없음.flexdirtest"), 96, CancellationToken.None));
    }

    // 디렉터리에는 썸네일이 없다. 호출자가 애초에 부르지 않지만(CanRequestThumbnail),
    // 부르더라도 아이콘으로 대체되어서는 안 된다.
    [Fact]
    public async Task Thumbnail_ForDirectory_IsNull()
    {
        Directory.CreateDirectory(Path.Combine(root, "하위"));

        using var source = new ShellThumbnailSource();

        Assert.Null(await source.GetThumbnailAsync(Location("하위"), 96, CancellationToken.None));
    }

    // ── 실패·취소·방어 ─────────────────────────────────────────────

    // 썸네일 하나 때문에 목록이 무너지면 안 된다. COM·P/Invoke 위에 서므로 무엇이든 나올 수
    // 있고, 계약은 "실패는 예외가 아니라 null" 이다.
    [Fact]
    public async Task FailedTypeIconLookup_YieldsNull()
    {
        using var source = new ShellThumbnailSource(
            (_, _, _) => throw new InvalidOperationException("shell 실패"),
            (_, _) => null,
            (_, _) => null);

        Assert.Null(await source.GetTypeIconAsync("txt", false, 16, CancellationToken.None));
    }

    [Fact]
    public async Task FailedThumbnailLookup_YieldsNull()
    {
        using var source = new ShellThumbnailSource(
            (_, _, _) => null,
            (_, _) => throw new InvalidOperationException("shell 실패"),
            (_, _) => null);

        Assert.Null(await source.GetThumbnailAsync(Location("a.bmp"), 96, CancellationToken.None));
    }

    [Fact]
    public async Task FailedItemIconLookup_YieldsNull()
    {
        using var source = new ShellThumbnailSource(
            (_, _, _) => null,
            (_, _) => null,
            (_, _) => throw new InvalidOperationException("shell 실패"));

        Assert.Null(await source.GetItemIconAsync(Location("폴더"), 32, CancellationToken.None));
    }

    // 취소는 실패와 다르다. 조용히 null 로 돌아오면 호출자가 그것을 "시도했고 없었다" 로
    // 세고 다시 묻지 않는다 — 스크롤로 돌아와도 영영 빈칸이 된다.
    [Fact]
    public async Task TypeIcon_CanceledToken_Throws()
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.GetTypeIconAsync("txt", false, 16, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Thumbnail_CanceledToken_Throws()
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.GetThumbnailAsync(Location("a.bmp"), 96, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ItemIcon_CanceledToken_Throws()
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.GetItemIconAsync(Location("폴더"), 32, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task NullExtension_Throws()
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await source.GetTypeIconAsync(null!, false, 16, CancellationToken.None));
    }

    [Fact]
    public async Task NullItem_Throws()
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await source.GetThumbnailAsync(null!, 96, CancellationToken.None));
    }

    // 0 이나 음수로 DIB 를 만들면 GDI 가 조용히 실패하거나 버퍼 길이가 어긋난다.
    // 만드는 자리에서 걸러야 원인이 남는다.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveSize_Throws(int size)
    {
        using var source = new ShellThumbnailSource();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await source.GetTypeIconAsync("txt", false, size, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await source.GetThumbnailAsync(Location("a.bmp"), size, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await source.GetItemIconAsync(Location("폴더"), size, CancellationToken.None));
    }

    [Fact]
    public void ImplementsThePort()
    {
        using var source = new ShellThumbnailSource();

        Assert.IsAssignableFrom<IThumbnailSource>(source);
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    private LocationId Location(string name)
    {
        Assert.True(LocationId.TryParse(Path.Combine(root, name), out var location, out _), name);

        return location;
    }

    private static LocationId Parse(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);

        return location;
    }

    /// <summary>
    /// 24비트 BMP 를 손으로 쓴다. 인코더를 끌어오지 않으려는 것이다 — BMP 는 헤더 54바이트에
    /// 픽셀이 바로 붙고 체크섬이 없어 이 자리에서 만들 수 있는 유일한 실제 이미지 형식이다.
    /// 가로 폭을 4의 배수로만 쓰므로 행 패딩은 필요 없다.
    /// </summary>
    private LocationId WriteBitmap(string name, int side)
    {
        const int HeaderSize = 54;

        var stride = side * 3;
        var pixels = new byte[stride * side];

        // 단색은 썸네일 추출기가 건너뛸 수 있다. 대각선 무늬를 넣어 내용이 있게 한다.
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                pixels[(y * stride) + (x * 3)] = (byte)(x * 8);
                pixels[(y * stride) + (x * 3) + 1] = (byte)(y * 8);
                pixels[(y * stride) + (x * 3) + 2] = (byte)((x + y) * 4);
            }
        }

        using (var file = File.Create(Path.Combine(root, name)))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(HeaderSize + pixels.Length);
            writer.Write(0);
            writer.Write(HeaderSize);

            writer.Write(40);           // BITMAPINFOHEADER
            writer.Write(side);
            writer.Write(side);
            writer.Write((short)1);
            writer.Write((short)24);
            writer.Write(0);            // BI_RGB
            writer.Write(pixels.Length);
            writer.Write(2835);         // 72 DPI
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);

            writer.Write(pixels);
        }

        return Location(name);
    }
}
