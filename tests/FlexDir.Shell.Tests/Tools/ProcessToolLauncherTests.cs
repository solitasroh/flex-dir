using System.ComponentModel;
using System.Diagnostics;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Tools;
using FlexDir.Shell.Tools;

using Xunit;

namespace FlexDir.Shell.Tests.Tools;

/// <summary>
/// <see cref="ProcessToolLauncher"/> — 탐지된 실행 파일을 현재 폴더에서 띄운다.
///
/// <para>
/// <b>실제로 띄우지 않는다.</b> 성공하면 터미널 창이 뜨고 그것은 게이트가 돌 때마다
/// 일어난다 (<c>.harness/HANDOFF.md</c> §규칙 5). 그래서 실행 지점을 <c>internal</c>
/// 생성자로 바꿔 끼우고 — <see cref="FlexDir.Shell.Activation.ShellItemActivator"/> 와
/// 같은 수다 — 재는 것은 <b>무엇을 실어 보내는가</b> 와 <b>실패를 어떻게 접는가</b> 뿐이다.
/// </para>
///
/// <para>
/// <c>ProcessStartInfo</c> 를 실제로 <c>Process.Start</c> 에 넘기는 한 줄은 여기서 닿지
/// 않는다. 사람이 실물로 확인한다.
/// </para>
/// </summary>
public sealed class ProcessToolLauncherTests
{
    private const string Terminal = @"C:\Program Files\WindowsApps\wt.exe";

    // 공백이 든 경로가 따옴표에 싸여 있다 — 우리가 다시 싸거나 쪼개면 여기서 드러난다.
    private const string Arguments = @"-d ""C:\Temp\a b""";

    private const int ErrorFileNotFound = 2;
    private const int ErrorAccessDenied = 5;
    private const int ErrorCancelled = 1223;

    // ── 성공 ────────────────────────────────────────────────────────

    [Fact]
    public async Task Launch_WhenTheProcessStarts_IsNotAnError()
    {
        var launcher = new ProcessToolLauncher(_ => { });

        var error = await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.None, error);
    }

    // ── 무엇을 싣는가 ───────────────────────────────────────────────

    // 인자는 step 0 의 FormatArguments 가 이미 치환을 끝낸 한 줄이다. 실행기가 손대면
    // 사용자가 적은 따옴표 규칙과 갈린다 (ArgumentList 를 쓰지 않는 이유와 같다).
    [Fact]
    public async Task Launch_CarriesTheExecutableAndArgumentsUntouched()
    {
        var started = Capture(out var launcher);

        await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(Terminal, started.Value!.FileName);
        Assert.Equal(Arguments, started.Value.Arguments);
    }

    /// <summary>
    /// <b><c>\\?\</c> 가 새면 안 된다.</b> 확장 접두사가 붙은 경로를 작업 디렉터리로 받는
    /// 프로그램은 거의 없다 — <c>LocationId.Value</c> 가 아니라 <c>DisplayPath</c> 다.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp")]
    [InlineData(@"\\10.10.10.23\home", @"\\10.10.10.23\home")]
    public async Task Launch_WorksFromTheFolderAsTheUserSeesIt(string path, string expected)
    {
        var started = Capture(out var launcher);

        await launcher.LaunchAsync(Terminal, Arguments, Folder(path), CancellationToken.None);

        Assert.Equal(expected, started.Value!.WorkingDirectory);
        Assert.DoesNotContain(@"\\?\", started.Value.WorkingDirectory, StringComparison.Ordinal);
    }

    // shell 로 실행하면 작업 디렉터리가 무시되는 경로가 있고, 실패가 Win32Exception 이
    // 아니라 shell 대화상자로 나온다 — 모달 금지 위반이자 --blame-hang 감이다.
    [Fact]
    public async Task Launch_DoesNotGoThroughTheShell()
    {
        var started = Capture(out var launcher);

        await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.False(started.Value!.UseShellExecute);
    }

    // 인자가 없는 프리셋이 셋이다 (powershell.exe · cmd.exe · git-bash.exe).
    [Fact]
    public async Task Launch_WithNoArguments_IsNotAnError()
    {
        var started = Capture(out var launcher);

        var error = await launcher.LaunchAsync(
            @"C:\Windows\System32\cmd.exe",
            string.Empty,
            Folder(@"C:\Temp"),
            CancellationToken.None);

        Assert.Equal(LocationErrorKind.None, error);
        Assert.Equal(string.Empty, started.Value!.Arguments);
    }

    // ── 실패 ────────────────────────────────────────────────────────

    // 매핑 표는 Win32ErrorMapping 하나뿐이다 — 여기에 두 번째 표를 만들지 않는다.
    [Theory]
    [InlineData(ErrorFileNotFound, LocationErrorKind.NotFound)]
    [InlineData(ErrorAccessDenied, LocationErrorKind.AccessDenied)]
    [InlineData(ErrorCancelled, LocationErrorKind.Unknown)]
    public async Task Launch_WhenTheProcessCannotStart_ReportsTheReason(int win32, LocationErrorKind expected)
    {
        var launcher = new ProcessToolLauncher(_ => throw new Win32Exception(win32));

        var error = await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(expected, error);
    }

    /// <summary>
    /// <b>코드 없는 실패를 성공으로 내면 안 된다.</b> <c>Classify(0)</c> 은
    /// <see cref="LocationErrorKind.None"/> 이고 이 포트에서 그것은 <b>성공</b>이다 —
    /// 그대로 내보내면 아무것도 안 뜬 채 상태표시줄도 조용하다.
    /// <see cref="FlexDir.Shell.Activation.ShellItemActivator"/> 가 같은 자리에서 같은
    /// 판단을 한다.
    /// </summary>
    [Fact]
    public async Task Launch_FailureWithoutACode_IsNotReportedAsSuccess()
    {
        var launcher = new ProcessToolLauncher(_ => throw new Win32Exception(0));

        var error = await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.Unknown, error);
    }

    // 프로그램을 지운 뒤 설정에 이름만 남은 상태가 정상이다 — 그것은 "없다" 이지
    // "알 수 없다" 가 아니다.
    [Theory]
    [MemberData(nameof(MissingThings))]
    public async Task Launch_WhenNothingIsThere_IsNotFound(Exception thrown)
    {
        var launcher = new ProcessToolLauncher(_ => throw thrown);

        var error = await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.NotFound, error);
    }

    public static TheoryData<Exception> MissingThings() =>
    [
        new FileNotFoundException("실행 파일이 없다."),
        new DirectoryNotFoundException("작업 디렉터리가 없다."),
        new InvalidOperationException("띄울 것이 정해지지 않았다."),
        new PlatformNotSupportedException("여기서는 못 띄운다."),
    ];

    // 실행 실패의 예외 공간은 위 넷보다 넓다. 남는 것은 전부 Unknown 이다 —
    // 예외 문자열을 상태표시줄에 흘리지 않는다.
    [Fact]
    public async Task Launch_WhenSomethingElseGoesWrong_IsUnknown()
    {
        var launcher = new ProcessToolLauncher(_ => throw new InvalidDataException("모르는 실패."));

        var error = await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.Unknown, error);
    }

    // ── 취소 ────────────────────────────────────────────────────────

    // 포트 계약 1 의 유일한 예외다. Unknown 으로 접으면 취소가 오류 문구가 된다.
    [Fact]
    public async Task Launch_WhenCancelledMidway_LetsTheCancellationOut()
    {
        var launcher = new ProcessToolLauncher(_ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None));
    }

    // 이미 취소된 뒤에 창이 뜨면 사용자는 취소한 일이 실행되는 것을 본다.
    [Fact]
    public async Task CanceledToken_DoesNotStartAnything()
    {
        var started = Capture(out var launcher);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), cts.Token));

        Assert.Null(started.Value);
    }

    // ── 띄울 것이 없을 때 ───────────────────────────────────────────

    /// <summary>
    /// <b>실행 파일이 비어 있는 것은 정상 상태다.</b> <c>Custom</c> 프리셋을 고르고 아직
    /// 경로를 안 적은 중간이 그렇다. 프로세스를 만들려 들면 예외 문자열이 상태표시줄에
    /// 뜬다 — 묻지도 말고 "없다" 로 답한다.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Launch_WithNothingToRun_DoesNotEvenTry(string? executable)
    {
        var started = Capture(out var launcher);

        var error = await launcher.LaunchAsync(executable!, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.Equal(LocationErrorKind.NotFound, error);
        Assert.Null(started.Value);
    }

    // ── 기다리지 않는다 · 스레드 ────────────────────────────────────

    /// <summary>
    /// <b><c>WaitForExit</c> 를 부르지 않는다</b> (포트 계약 2). 부르면 그 터미널이 닫힐
    /// 때까지 호출이 묶인다 — 실물에서는 무한이고 여기서는 시간으로만 보인다. 그래서
    /// 실행 지점이 돌아온 순간부터 <see cref="ProcessToolLauncher.LaunchAsync"/> 가
    /// 끝날 때까지를 잰다.
    /// </summary>
    [Fact]
    public async Task Launch_DoesNotWaitForTheProgramToExit()
    {
        var stopwatch = new Stopwatch();

        var launcher = new ProcessToolLauncher(_ => stopwatch.Restart());

        await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"돌아오는 데 {stopwatch.Elapsed} 걸렸다.");
    }

    /// <summary>
    /// 프로세스 생성은 UI 스레드 밖이어야 한다 (CLAUDE.md §3) — 실행 파일이 네트워크
    /// 경로에 있으면 초 단위로 블로킹된다. <b>STA 는 필요 없다</b>: COM 이 아니므로
    /// 스레드풀이면 된다 (§규칙 8 — 아파트먼트와 블로킹은 다른 문제다).
    /// </summary>
    [Fact]
    public async Task Launch_RunsOffTheCallersThread()
    {
        var caller = Environment.CurrentManagedThreadId;
        var ran = 0;
        var pooled = false;

        var launcher = new ProcessToolLauncher(_ =>
        {
            ran = Environment.CurrentManagedThreadId;
            pooled = Thread.CurrentThread.IsThreadPoolThread;
        });

        await launcher.LaunchAsync(Terminal, Arguments, Folder(@"C:\Temp"), CancellationToken.None);

        Assert.NotEqual(caller, ran);
        Assert.True(pooled);
    }

    // ── 방어 ────────────────────────────────────────────────────────

    [Fact]
    public async Task NullFolder_Throws()
    {
        var launcher = new ProcessToolLauncher(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await launcher.LaunchAsync(Terminal, Arguments, null!, CancellationToken.None));
    }

    [Fact]
    public void ImplementsThePort()
    {
        Assert.IsAssignableFrom<IExternalToolLauncher>(new ProcessToolLauncher());
    }

    // ── 도구 ────────────────────────────────────────────────────────

    /// <summary>
    /// 실행 지점에 실린 <see cref="ProcessStartInfo"/> 를 잡아 둔다. 안 불렸으면
    /// <c>Value</c> 가 <see langword="null"/> 이고, 그것으로 "아예 안 띄웠다" 를 잰다.
    /// </summary>
    private static Started Capture(out ProcessToolLauncher launcher)
    {
        var started = new Started();

        launcher = new ProcessToolLauncher(info => started.Value = info);

        return started;
    }

    private sealed class Started
    {
        public ProcessStartInfo? Value { get; set; }
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);

        return location;
    }
}
