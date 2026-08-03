namespace FlexDir.Core.Errors;

/// <summary>
/// Win32 오류 코드를 <see cref="LocationErrorKind"/> 로 옮긴다.
/// <para>
/// 코드는 호출자가 <see cref="int"/> 로 넘긴다 — <c>FlexDir.Core</c> 는 Windows API 를
/// 참조하지 않으므로 (CLAUDE.md §1) <c>Marshal.GetLastWin32Error</c>·<c>Win32Exception</c> 을
/// 여기서 부르지 않는다.
/// </para>
/// <para>
/// v1 이 실제로 다루는 코드만 넣는다. 네트워크 코드(53·67·1326·1219 등)는 v1 이 로컬
/// 파일시스템만 다루므로 (docs/PRD.md §3) <see cref="LocationErrorKind.Unknown"/> 으로 남긴다 —
/// 지금 넣으면 실행되지 않는 분기가 남고, v2 에서 실물 검증 없이 옳다고 믿게 된다.
/// 표는 docs/SHELL_NOTES.md §오류 코드 매핑 에 이미 있으니 그때 쓴다.
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

        _ => LocationErrorKind.Unknown,
    };
}
