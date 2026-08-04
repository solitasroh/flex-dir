using FlexDir.Core.Presentation;
using FlexDir.Shell.Presentation;

using Xunit;

namespace FlexDir.Shell.Tests.Presentation;

/// <summary>
/// Windows Shell 에서 유형 이름을 얻는 구현체.
/// <para>
/// <b>문구 자체는 검증하지 않는다.</b> "텍스트 문서" 는 로케일과 설치된 프로그램에 따라
/// 달라지므로 기계마다 다른 값을 게이트에 넣을 수 없다. 대신 <b>성질</b>을 잰다 —
/// 비어 있지 않은가, 확장자마다 갈리는가, 대소문자를 같은 것으로 보는가, 확장자마다
/// 한 번만 묻는가, 그리고 <b>STA 에서 도는가</b>.
/// </para>
/// <para>
/// 조회 자체를 바꿔 끼울 수 있게 열어둔 이유는 캐시와 실패 처리를 결정적으로 재기
/// 위해서다. shell 호출은 이 기계에 무엇이 설치됐는지에 따라 결과가 달라진다.
/// </para>
/// </summary>
public sealed class ShellTypeNameProviderTests
{
    // ── 아파트먼트 ──────────────────────────────────────────────────
    // SHGetFileInfo 는 STA 를 요구하고 스레드풀은 MTA 다 (docs/SHELL_NOTES.md §COM 아파트먼트).
    // Task.Run 으로 돌리면 이 테스트가 실패한다 — 실제로 그렇게 썼다가 여기서 잡혔다.

    [Fact]
    public async Task Lookup_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var provider = new ShellTypeNameProvider((_, _) =>
        {
            apartment = Thread.CurrentThread.GetApartmentState();

            return "유형";
        });

        await provider.GetTypeNameAsync("txt", false, CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    // ── 실제 shell 에 물어본다 ──────────────────────────────────────

    [Fact]
    public async Task KnownExtension_HasAName()
    {
        using var provider = new ShellTypeNameProvider();

        Assert.NotEmpty(await provider.GetTypeNameAsync("txt", false, CancellationToken.None));
    }

    [Fact]
    public async Task DifferentExtensions_HaveDifferentNames()
    {
        using var provider = new ShellTypeNameProvider();

        var text = await provider.GetTypeNameAsync("txt", false, CancellationToken.None);
        var executable = await provider.GetTypeNameAsync("exe", false, CancellationToken.None);

        Assert.NotEqual(text, executable);
    }

    // 유형 컬럼과 아이콘 캐시가 확장자를 키로 쓰므로 .JPG 와 .jpg 가 갈라지면 안 된다.
    [Fact]
    public async Task ExtensionCase_DoesNotMatter()
    {
        using var provider = new ShellTypeNameProvider();

        Assert.Equal(
            await provider.GetTypeNameAsync("txt", false, CancellationToken.None),
            await provider.GetTypeNameAsync("TXT", false, CancellationToken.None));
    }

    // 디렉터리도 확장자가 빈 문자열로 온다. isDirectory 로 갈라야 "파일 폴더" 와
    // 확장자 없는 파일이 구분된다.
    [Fact]
    public async Task Directory_AndExtensionlessFile_AreDifferent()
    {
        using var provider = new ShellTypeNameProvider();

        var folder = await provider.GetTypeNameAsync(string.Empty, true, CancellationToken.None);
        var file = await provider.GetTypeNameAsync(string.Empty, false, CancellationToken.None);

        Assert.NotEmpty(folder);
        Assert.NotEmpty(file);
        Assert.NotEqual(folder, file);
    }

    // 등록되지 않은 확장자도 Windows 가 이름을 만들어 준다("ZZZZZ 파일"). 빈칸이 아니어야
    // 유형 컬럼이 뚫려 보이지 않는다.
    [Fact]
    public async Task UnregisteredExtension_StillHasAName()
    {
        using var provider = new ShellTypeNameProvider();

        Assert.NotEmpty(await provider.GetTypeNameAsync("zzzzz", false, CancellationToken.None));
    }

    // 파일을 건드리지 않는다 — 센티널 이름 + SHGFI_USEFILEATTRIBUTES (SHELL_NOTES §아이콘).
    // 존재하지 않는 확장자로도 이름이 나오는 것이 그 증거다.
    [Fact]
    public async Task NoFileNeedsToExist()
    {
        using var provider = new ShellTypeNameProvider();

        Assert.NotEmpty(await provider.GetTypeNameAsync("존재하지않는확장자", false, CancellationToken.None));
    }

    // ── 캐시·실패·취소 (조회를 바꿔 끼워 결정적으로 잰다) ───────────

    [Fact]
    public async Task SameExtension_IsLookedUpOnce()
    {
        var calls = 0;

        using var provider = new ShellTypeNameProvider((_, _) =>
        {
            calls++;

            return "유형";
        });

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal("유형", await provider.GetTypeNameAsync("txt", false, CancellationToken.None));
        }

        // 10만 항목 폴더에서 확장자가 열 종류면 조회도 열 번이어야 한다.
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DirectoryAndFile_AreCachedSeparately()
    {
        var calls = 0;

        using var provider = new ShellTypeNameProvider((_, isDirectory) =>
        {
            calls++;

            return isDirectory ? "폴더" : "파일";
        });

        await provider.GetTypeNameAsync(string.Empty, true, CancellationToken.None);
        await provider.GetTypeNameAsync(string.Empty, false, CancellationToken.None);
        await provider.GetTypeNameAsync(string.Empty, true, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    // 유형 이름 하나 때문에 폴더가 통째로 열리지 않으면 안 된다. 구현체는 COM 위에 서므로
    // 계약을 어기는 예외가 나올 수 있고, 그것을 여기서 삼킨다.
    [Fact]
    public async Task FailedLookup_YieldsEmptyString()
    {
        using var provider = new ShellTypeNameProvider((_, _) => throw new InvalidOperationException("shell 실패"));

        Assert.Equal(
            string.Empty,
            await provider.GetTypeNameAsync("txt", false, CancellationToken.None));
    }

    // 실패도 캐시한다 — 확장자마다 한 번이라는 규칙은 실패에도 적용된다. 그러지 않으면
    // 실패하는 확장자가 목록에 100개 있을 때 조회도 100번 나간다.
    [Fact]
    public async Task FailedLookup_IsNotRetried()
    {
        var calls = 0;

        using var provider = new ShellTypeNameProvider((_, _) =>
        {
            calls++;

            throw new InvalidOperationException("shell 실패");
        });

        await provider.GetTypeNameAsync("txt", false, CancellationToken.None);
        await provider.GetTypeNameAsync("txt", false, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    // 취소는 실패와 다르다. 조용히 빈 문자열로 돌아오면 이미 떠난 폴더의 줄을 계속 만든다.
    [Fact]
    public async Task CanceledToken_Throws()
    {
        using var provider = new ShellTypeNameProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await provider.GetTypeNameAsync("txt", false, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task NullExtension_Throws()
    {
        using var provider = new ShellTypeNameProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await provider.GetTypeNameAsync(null!, false, CancellationToken.None));
    }

    [Fact]
    public void ImplementsThePort()
    {
        using var provider = new ShellTypeNameProvider();

        Assert.IsAssignableFrom<ITypeNameProvider>(provider);
    }
}
