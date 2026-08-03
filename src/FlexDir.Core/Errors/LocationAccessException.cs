using FlexDir.Core.Locations;

namespace FlexDir.Core.Errors;

/// <summary>
/// 열거·탐색 계층이 던지는 예외. 다음 phase 의 <c>IFolderSource</c> 가 이것을 쓴다.
/// <para>
/// 잡는 쪽은 <see cref="Kind"/> 로 사용자에게 할 말을, <see cref="Win32Error"/> 로 진단을 얻는다.
/// 오류를 만나 이전 경로로 되돌리는 판단은 여기 없다 — 경로는 그대로 두고
/// (docs/PRD.md §4) 탐색 정책은 ViewModel 의 일이다.
/// </para>
/// </summary>
public sealed class LocationAccessException : Exception
{
    /// <param name="win32Error">
    /// 원본 코드. 없으면 0 — 상위 계층이 분류만 알고 던지는 경로가 있다.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="kind"/> 가 <see cref="LocationErrorKind.None"/> 일 때.
    /// 오류가 아닌 것을 던지는 호출은 버그다.
    /// </exception>
    public LocationAccessException(LocationErrorKind kind, LocationId location, int win32Error = 0)
        : base(LocationErrorMessages.Describe(kind, location))
    {
        Kind = kind;
        Location = location;
        Win32Error = win32Error;
    }

    public LocationErrorKind Kind { get; }

    public LocationId Location { get; }

    /// <summary>원본 Win32 코드. 0 이면 코드 없이 분류만 온 것이다.</summary>
    public int Win32Error { get; }
}
