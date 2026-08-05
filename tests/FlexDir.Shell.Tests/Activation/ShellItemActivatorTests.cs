using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Activation;

using Xunit;

namespace FlexDir.Shell.Tests.Activation;

/// <summary>
/// <c>ShellExecuteEx</c> 로 연결 프로그램을 여는 구현체.
/// <para>
/// <b>실제로 실행해 보지 않는다.</b> 성공하면 프로그램이 뜨고 실패하면 shell 이 자기
/// 오류 대화상자를 띄운다 — 둘 다 자동 테스트에서 일어나서는 안 되는 일이다(대화상자는
/// 답을 기다리며 블로킹하므로 <c>--blame-hang</c> 에 걸린다). 그래서 실행 지점을
/// 바꿔 끼우고, 재는 것은 <b>shell 이 낸 코드를 어떻게 옮기는가</b> 다.
/// </para>
/// <para>
/// P/Invoke 자체(구조체 크기·마스크·작업 디렉터리)는 여기서 닿지 않는다.
/// <c>.harness/probe activate &lt;경로&gt;</c> 로 사람이 확인하고 결과를
/// <c>.harness/manual-plan.md</c> 에 적는다.
/// </para>
/// </summary>
public sealed class ShellItemActivatorTests
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNoAssociation = 1155;
    private const int ErrorCancelled = 1223;

    // ── 아파트먼트 ──────────────────────────────────────────────────
    // ShellExecuteEx 는 SEE_MASK_INVOKEIDLIST 로 항목의 IContextMenu 를 타므로 STA 를
    // 요구한다 (docs/SHELL_NOTES.md §COM 아파트먼트). 스레드풀은 MTA 다.

    [Fact]
    public async Task Activate_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var activator = new ShellItemActivator(_ =>
        {
            apartment = Thread.CurrentThread.GetApartmentState();

            return null;
        });

        await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    // ── 무엇을 넘기는가 ─────────────────────────────────────────────

    [Fact]
    public async Task Activate_HandsTheItemToTheShell()
    {
        LocationId? seen = null;

        using var activator = new ShellItemActivator(item =>
        {
            seen = item;

            return null;
        });

        var target = Location(@"C:\Temp\문서.txt");

        await activator.ActivateAsync(target, CancellationToken.None);

        Assert.Equal(target, seen);
    }

    [Fact]
    public async Task Activate_WhenTheShellSucceeds_DoesNotThrow()
    {
        using var activator = new ShellItemActivator(_ => null);

        await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None);
    }

    // ── 실패가 아닌 실패 ────────────────────────────────────────────

    // 연결된 프로그램이 없으면 shell 이 '연결 프로그램' 대화상자를 띄우고 1155 로 돌아온다.
    // 그것은 사용자가 답해야 할 일이지 우리가 상태표시줄에 오류로 적을 일이 아니다.
    [Fact]
    public async Task Activate_WithNoAssociation_IsNotAnError()
    {
        using var activator = new ShellItemActivator(_ => ErrorNoAssociation);

        await activator.ActivateAsync(Location(@"C:\Temp\a.알수없음"), CancellationToken.None);
    }

    // 사용자가 그 대화상자를 닫으면 1223 이다. 취소는 실패가 아니다 —
    // 오류 문구를 띄우면 사용자가 방금 스스로 한 선택을 오류로 통보받는다.
    [Fact]
    public async Task Activate_WhenTheUserCancels_IsNotAnError()
    {
        using var activator = new ShellItemActivator(_ => ErrorCancelled);

        await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None);
    }

    // ── 실패 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ErrorFileNotFound, LocationErrorKind.NotFound)]
    [InlineData(ErrorPathNotFound, LocationErrorKind.NotFound)]
    [InlineData(ErrorAccessDenied, LocationErrorKind.AccessDenied)]
    public async Task Activate_MappedError_KeepsItsKind(int win32, LocationErrorKind expected)
    {
        using var activator = new ShellItemActivator(_ => win32);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(win32, error.Win32Error);
    }

    // 실행 실패의 코드 공간은 열거보다 넓다 — 매핑에 없는 코드가 정상적으로 온다.
    [Fact]
    public async Task Activate_UnmappedError_BecomesUnknown()
    {
        const int ErrorBadExeFormat = 193;

        using var activator = new ShellItemActivator(_ => ErrorBadExeFormat);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await activator.ActivateAsync(Location(@"C:\Temp\a.exe"), CancellationToken.None));

        Assert.Equal(LocationErrorKind.Unknown, error.Kind);
        Assert.Equal(ErrorBadExeFormat, error.Win32Error);
    }

    // <b>0 을 그대로 넘기면 예외가 바뀐다.</b> Win32ErrorMapping.Classify(0) 은 None 이고
    // LocationAccessException 은 None 을 받으면 ArgumentException 을 던진다 — 실행 실패가
    // 인자 오류로 둔갑해 진짜 원인이 사라진다. ShellExecuteEx 가 실패하고도
    // GetLastError 가 0 인 경로는 실재한다.
    [Fact]
    public async Task Activate_FailureWithoutACode_BecomesUnknown()
    {
        using var activator = new ShellItemActivator(_ => 0);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None));

        Assert.Equal(LocationErrorKind.Unknown, error.Kind);
        Assert.Equal(0, error.Win32Error);
    }

    // 상태표시줄 문구는 위치를 그대로 쓴다 (docs/UI_GUIDE.md §상태 표현).
    [Fact]
    public async Task Activate_Failure_CarriesTheItem()
    {
        var target = Location(@"C:\Temp\없는파일.txt");

        using var activator = new ShellItemActivator(_ => ErrorFileNotFound);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await activator.ActivateAsync(target, CancellationToken.None));

        Assert.Equal(target, error.Location);
        Assert.Contains(@"C:\Temp\없는파일.txt", error.Message);
    }

    // ── 취소·방어 ───────────────────────────────────────────────────

    // 이미 취소된 뒤에 프로그램이 뜨면 사용자는 취소한 일이 실행되는 것을 본다.
    [Fact]
    public async Task CanceledToken_DoesNotReachTheShell()
    {
        var launched = false;

        using var activator = new ShellItemActivator(_ =>
        {
            launched = true;

            return null;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await activator.ActivateAsync(
                Location(@"C:\Temp\a.txt"),
                new CancellationToken(canceled: true)));

        Assert.False(launched);
    }

    [Fact]
    public async Task NullItem_Throws()
    {
        using var activator = new ShellItemActivator(_ => null);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await activator.ActivateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AfterDispose_Throws()
    {
        var activator = new ShellItemActivator(_ => null);

        activator.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await activator.ActivateAsync(Location(@"C:\Temp\a.txt"), CancellationToken.None));
    }

    // 두 번 Dispose 해도 조용해야 한다 — Host 의 조립이 소유권을 어떻게 잡든 종료가
    // 예외로 끝나면 안 된다 (ADR-003 상주 프로세스).
    [Fact]
    public void DisposeIsIdempotent()
    {
        var activator = new ShellItemActivator(_ => null);

        activator.Dispose();
        activator.Dispose();
    }

    [Fact]
    public void ImplementsThePort()
    {
        using var activator = new ShellItemActivator();

        Assert.IsAssignableFrom<IItemActivator>(activator);
    }

    private static LocationId Location(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);

        return location;
    }
}
