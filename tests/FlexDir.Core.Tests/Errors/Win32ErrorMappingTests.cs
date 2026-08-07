using FlexDir.Core.Errors;

using Xunit;

namespace FlexDir.Core.Tests.Errors;

/// <summary>
/// Win32 오류 코드 → 분류. 경로 문제와 권한 문제를 구분하는 것이 목적이다
/// (docs/SHELL_NOTES.md §오류 코드 매핑).
/// 네트워크 코드는 <b>실물에서 본 것만</b> 매핑한다 (docs/PRD-v2.md §5 N-4).
/// </summary>
public class Win32ErrorMappingTests
{
    [Theory]
    [InlineData(0, LocationErrorKind.None)]              // ERROR_SUCCESS
    [InlineData(2, LocationErrorKind.NotFound)]          // ERROR_FILE_NOT_FOUND
    [InlineData(3, LocationErrorKind.NotFound)]          // ERROR_PATH_NOT_FOUND
    [InlineData(5, LocationErrorKind.AccessDenied)]      // ERROR_ACCESS_DENIED
    [InlineData(15, LocationErrorKind.NotFound)]         // ERROR_INVALID_DRIVE
    [InlineData(19, LocationErrorKind.AccessDenied)]     // ERROR_WRITE_PROTECT
    [InlineData(21, LocationErrorKind.DeviceNotReady)]   // ERROR_NOT_READY
    [InlineData(32, LocationErrorKind.Sharing)]          // ERROR_SHARING_VIOLATION
    [InlineData(1314, LocationErrorKind.AccessDenied)]   // ERROR_PRIVILEGE_NOT_HELD
    public void Classify_MapsTheV1Codes(int win32Error, LocationErrorKind expected)
    {
        Assert.Equal(expected, Win32ErrorMapping.Classify(win32Error));
    }

    [Theory]
    [InlineData(1223)]   // ERROR_CANCELLED — 취소는 오류 분류가 아니라 취소 토큰의 일이다
    [InlineData(87)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Classify_UnmappedCodeIsUnknown(int win32Error)
    {
        Assert.Equal(LocationErrorKind.Unknown, Win32ErrorMapping.Classify(win32Error));
    }

    // ── 네트워크 코드 (docs/PRD-v2.md §5 N-4) ───────────────────────
    // v1 은 전부 Unknown 으로 남겨 뒀다 — "실물 검증 없이 옳다고 믿게 된다" 는 이유였다.
    // 아래 셋은 그 검증을 마쳤다 (2026-08-07 · \\10.10.10.23).

    [Theory]
    [InlineData(53, LocationErrorKind.NotFound)]                  // ERROR_BAD_NETPATH — 없는 서버
    [InlineData(67, LocationErrorKind.NotFound)]                  // ERROR_BAD_NET_NAME — 없는 공유
    [InlineData(1219, LocationErrorKind.CredentialConflict)]      // ERROR_SESSION_CREDENTIAL_CONFLICT
    public void Classify_MapsTheNetworkCodesThatWereMeasured(int win32Error, LocationErrorKind expected)
    {
        Assert.Equal(expected, Win32ErrorMapping.Classify(win32Error));
    }

    [Theory]
    [InlineData(1326)]   // ERROR_LOGON_FAILURE
    [InlineData(1311)]   // ERROR_NO_LOGON_SERVERS
    [InlineData(1231)]   // ERROR_NETWORK_UNREACHABLE
    [InlineData(1232)]   // ERROR_HOST_UNREACHABLE
    public void Classify_UnmeasuredNetworkCodesStayUnknown(int win32Error)
    {
        // 이 경로(FindFirstFileExW)에서 이 코드들이 실제로 오는 것을 아직 못 봤다.
        // 짐작으로 넣으면 틀려도 드러나지 않는다 — 공유 열거(WNet)의 표와 다른 이유가 이것이다.
        Assert.Equal(LocationErrorKind.Unknown, Win32ErrorMapping.Classify(win32Error));
    }

    [Fact]
    public void Classify_SuccessIsTheOnlyCodeThatIsNotAnError()
    {
        Assert.Equal(LocationErrorKind.None, Win32ErrorMapping.Classify(0));

        foreach (var code in new[] { 2, 3, 5, 15, 19, 21, 32, 1314, 1223, 53 })
        {
            Assert.NotEqual(LocationErrorKind.None, Win32ErrorMapping.Classify(code));
        }
    }

    [Fact]
    public void Classify_IsPure()
    {
        // 같은 코드는 항상 같은 분류다 — Marshal.GetLastWin32Error 같은 주변 상태를 읽지 않는다.
        Assert.Equal(Win32ErrorMapping.Classify(5), Win32ErrorMapping.Classify(5));
    }
}
