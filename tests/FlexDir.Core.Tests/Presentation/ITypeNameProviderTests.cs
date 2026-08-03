using FlexDir.Core.Presentation;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Presentation;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// <c>FlexDir.Shell</c> 의 구현체도 같은 검증을 받는다.
/// <para>
/// 여기서 못 박는 것은 <b>조회 횟수를 셀 수 있다</b> 는 것이다. "확장자당 한 번만 조회한다"
/// 는 규칙은 호출자(<c>PaneViewModel</c>)가 지키는 것이고, 그 검증은 이 fake 의
/// <see cref="FakeTypeNameProvider.Calls"/> 에 의존한다. 세는 쪽이 먼저 믿을 수 있어야 한다.
/// </para>
/// </summary>
public class FakeTypeNameProviderTests
{
    [Fact]
    public async Task GetTypeNameAsync_ReturnsUppercasedExtension()
    {
        var provider = new FakeTypeNameProvider();

        Assert.Equal("TXT", await provider.GetTypeNameAsync("txt", isDirectory: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetTypeNameAsync_Directory_DoesNotLookAtTheExtension()
    {
        var provider = new FakeTypeNameProvider();

        // 디렉터리는 확장자가 없다 (FileItem.Extension 이 빈 문자열을 낸다).
        Assert.Equal("DIR", await provider.GetTypeNameAsync(string.Empty, isDirectory: true, CancellationToken.None));
    }

    [Fact]
    public async Task GetTypeNameAsync_ExtensionlessFile_IsNotTheSameAsADirectory()
    {
        var provider = new FakeTypeNameProvider();

        var file = await provider.GetTypeNameAsync(string.Empty, isDirectory: false, CancellationToken.None);
        var directory = await provider.GetTypeNameAsync(string.Empty, isDirectory: true, CancellationToken.None);

        Assert.NotEqual(directory, file);
    }

    [Fact]
    public async Task Calls_RecordEveryInvocation_SoRepeatedLookupsAreVisible()
    {
        var provider = new FakeTypeNameProvider();

        await provider.GetTypeNameAsync("txt", isDirectory: false, CancellationToken.None);
        await provider.GetTypeNameAsync("txt", isDirectory: false, CancellationToken.None);

        // fake 가 캐시해버리면 호출자의 중복 조회가 보이지 않는다.
        Assert.Equal(2, provider.CountFor("txt", isDirectory: false));
    }

    [Fact]
    public async Task Calls_CountEachExtensionSeparately()
    {
        var provider = new FakeTypeNameProvider();

        await provider.GetTypeNameAsync("txt", isDirectory: false, CancellationToken.None);
        await provider.GetTypeNameAsync("png", isDirectory: false, CancellationToken.None);
        await provider.GetTypeNameAsync(string.Empty, isDirectory: true, CancellationToken.None);

        Assert.Equal(
            [("txt", false), ("png", false), (string.Empty, true)],
            provider.Calls);
        Assert.Equal(1, provider.CountFor("png", isDirectory: false));
        Assert.Equal(0, provider.CountFor("png", isDirectory: true));
    }

    [Fact]
    public async Task GetTypeNameAsync_WhenTheLookupFails_ReturnsAnEmptyString()
    {
        var provider = new FakeTypeNameProvider();
        provider.UnknownExtensions.Add("xyz");

        // 계약이다 — 실패는 예외가 아니다. 예외로 만들면 호출자가 항목마다 잡아야 한다.
        Assert.Equal(string.Empty, await provider.GetTypeNameAsync("xyz", isDirectory: false, CancellationToken.None));

        // 실패도 조회다. 기록에 남지 않으면 "확장자당 한 번" 을 재는 눈금이 어긋난다.
        Assert.Equal(1, provider.CountFor("xyz", isDirectory: false));
    }

    [Fact]
    public async Task Failure_InjectsAContractViolation()
    {
        var provider = new FakeTypeNameProvider();
        provider.Failure = new InvalidOperationException("shell 이 답하지 않는다.");

        // 계약대로면 여기서 예외가 나오지 않는다. 구현체가 COM 위에 서므로 그럼에도
        // 나올 수 있고, 호출자의 방어를 재려면 그 상황을 만들 수 있어야 한다.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.GetTypeNameAsync("txt", isDirectory: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetTypeNameAsync_WhenAlreadyCanceled_Throws()
    {
        var provider = new FakeTypeNameProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await provider.GetTypeNameAsync("txt", isDirectory: false, cts.Token));

        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task GetTypeNameAsync_IsUsableThroughThePortAlone()
    {
        // 호출부는 fake 를 모른다. 포트만으로 쓸 수 있어야 한다.
        ITypeNameProvider port = new FakeTypeNameProvider();

        Assert.Equal("PNG", await port.GetTypeNameAsync("png", isDirectory: false, CancellationToken.None));
    }
}
