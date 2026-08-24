using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Tools;

/// <summary>
/// 실행 포트의 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>Process.Start</c>)는
/// <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009).
/// <para>
/// 계약 넷: ① 실패를 던지지 않고 <see cref="LocationErrorKind"/> 로 낸다 ② 기다리지
/// 않는다 ③ <c>workingFolder</c> 는 작업 디렉터리로만 쓴다 ④ 취소만 예외.
/// 이 중 ②·③ 은 fake 로 채점할 수 없으므로 <b>기록이 그대로인지</b>로 대신한다 —
/// 인자에 폴더가 덧붙는 순간 여기서 드러난다.
/// </para>
/// </summary>
public class FakeExternalToolLauncherTests
{
    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out _));
        return location;
    }

    [Fact]
    public async Task LaunchAsync_ByDefault_Succeeds()
    {
        var launcher = new FakeExternalToolLauncher();

        var result = await launcher.LaunchAsync("wt.exe", @"-d ""C:\Temp""", Parse(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.None, result);
    }

    /// <summary>
    /// 실패는 정상 상황이다 (프로그램을 지웠을 수 있다). 던지지 않고 사유를 내는 이유는
    /// 호출자가 그것을 상태표시줄 한 줄로 만들어야 하기 때문이다 — 대화상자는 없다.
    /// </summary>
    [Theory]
    [InlineData(LocationErrorKind.NotFound)]
    [InlineData(LocationErrorKind.AccessDenied)]
    [InlineData(LocationErrorKind.Unknown)]
    public async Task LaunchAsync_WithInjectedFailure_GivesThatKind(LocationErrorKind kind)
    {
        var launcher = new FakeExternalToolLauncher { Result = kind };

        var result = await launcher.LaunchAsync("code.exe", string.Empty, Parse(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(kind, result);
    }

    /// <summary>
    /// 셋 중 하나만 틀려도 실물에서는 엉뚱한 폴더에 터미널이 뜬다. 특히 인자는
    /// <b>이미 치환이 끝난</b> 문자열이라 실행기가 손대지 않는다.
    /// </summary>
    [Fact]
    public async Task LaunchAsync_RecordsExecutableArgumentsAndFolder()
    {
        var launcher = new FakeExternalToolLauncher();
        var folder = Parse(@"C:\Users\me\src");

        await launcher.LaunchAsync("pwsh.exe", @"-NoLogo -WorkingDirectory ""C:\Users\me\src""", folder, CancellationToken.None);

        var launch = Assert.Single(launcher.Launches);
        Assert.Equal("pwsh.exe", launch.Executable);
        Assert.Equal(@"-NoLogo -WorkingDirectory ""C:\Users\me\src""", launch.Arguments);
        Assert.Equal(folder, launch.Folder);
    }

    [Fact]
    public async Task LaunchAsync_WithCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var launcher = new FakeExternalToolLauncher();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await launcher.LaunchAsync("cmd.exe", string.Empty, Parse(@"C:\Temp"), cts.Token));
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task LaunchAsync_CalledSeveralTimes_KeepsOrder()
    {
        var launcher = new FakeExternalToolLauncher();

        await launcher.LaunchAsync("cmd.exe", string.Empty, Parse(@"C:\A"), CancellationToken.None);
        await launcher.LaunchAsync("code.exe", string.Empty, Parse(@"C:\B"), CancellationToken.None);

        Assert.Equal(["cmd.exe", "code.exe"], launcher.Launches.Select(l => l.Executable));
        Assert.Equal([Parse(@"C:\A"), Parse(@"C:\B")], launcher.Launches.Select(l => l.Folder));
    }
}
