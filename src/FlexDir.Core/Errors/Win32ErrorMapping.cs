namespace FlexDir.Core.Errors;

/// <summary>
/// Win32 오류 코드를 <see cref="LocationErrorKind"/> 로 옮긴다.
/// <para>
/// 코드는 호출자가 <see cref="int"/> 로 넘긴다 — <c>FlexDir.Core</c> 는 Windows API 를
/// 참조하지 않으므로 (CLAUDE.md §1) <c>Marshal.GetLastWin32Error</c>·<c>Win32Exception</c> 을
/// 여기서 부르지 않는다.
/// </para>
/// <para>
/// <b>실물에서 본 코드만 넣는다.</b> v1 은 네트워크 코드를 전부
/// <see cref="LocationErrorKind.Unknown"/> 으로 남겨 뒀다 — "지금 넣으면 실행되지 않는
/// 분기가 남고, v2 에서 실물 검증 없이 옳다고 믿게 된다" 는 이유였다. v2 가 그 기준을
/// 그대로 이어받아, 아래 셋만 <c>\\10.10.10.23</c> 에서 재현해 보고 넣었다
/// (2026-08-07 · docs/PRD-v2.md §5 N-4). 나머지는 여전히 <c>Unknown</c> 이다.
/// </para>
/// <para>
/// <c>NetworkShareSource</c> 는 코드 8종을 따로 갖는다. 같은 표를 두 벌 두는 것이 아니라
/// <b>다른 호출 경로</b>다 — 그쪽은 <c>WNetOpenEnum</c>(mpr.dll), 여기로 오는 것은
/// <c>FindFirstFileExW</c> 다. 어느 코드가 실제로 오는지가 경로마다 다르다.
/// </para>
/// </summary>
public static class Win32ErrorMapping
{
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidDrive = 15;
    private const int ErrorWriteProtect = 19;
    private const int ErrorNotReady = 21;
    private const int ErrorSharingViolation = 32;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorPrivilegeNotHeld = 1314;

    public static LocationErrorKind Classify(int win32Error) => win32Error switch
    {
        ErrorSuccess => LocationErrorKind.None,
        ErrorFileNotFound => LocationErrorKind.NotFound,
        ErrorPathNotFound => LocationErrorKind.NotFound,

        // 없는 드라이브 문자는 권한이 아니라 경로 문제다.
        ErrorInvalidDrive => LocationErrorKind.NotFound,

        ErrorAccessDenied => LocationErrorKind.AccessDenied,

        // 쓰기 금지 매체·권한 상승 미보유는 둘 다 "경로는 맞는데 못 한다" 다.
        ErrorWriteProtect => LocationErrorKind.AccessDenied,
        ErrorPrivilegeNotHeld => LocationErrorKind.AccessDenied,

        ErrorNotReady => LocationErrorKind.DeviceNotReady,
        ErrorSharingViolation => LocationErrorKind.Sharing,

        // 없는 서버(53)와 없는 공유(67)는 둘 다 경로 문제다 — 오타이거나 꺼진 서버다.
        // 실측: 없는 공유는 즉시, 없는 서버는 42초 걸려 돌아온다 (CLAUDE.md §3).
        ErrorBadNetPath => LocationErrorKind.NotFound,
        ErrorBadNetName => LocationErrorKind.NotFound,

        // AccessDenied 로 접지 않는다 — 비밀번호를 고쳐도 안 되는 종류다.
        ErrorSessionCredentialConflict => LocationErrorKind.CredentialConflict,

        _ => LocationErrorKind.Unknown,
    };
}
