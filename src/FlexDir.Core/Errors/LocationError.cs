using FlexDir.Core.Locations;

namespace FlexDir.Core.Errors;

/// <summary>
/// 열거·탐색 실패의 분류. <b>경로 문제와 권한 문제를 구분하는 것이 목적이다</b> —
/// 뭉뚱그리면 사용자가 손쓸 방법을 알 수 없다 (docs/SHELL_NOTES.md §오류 코드 매핑).
/// </summary>
public enum LocationErrorKind
{
    None,

    /// <summary>권한 문제 — 경로는 맞다.</summary>
    AccessDenied,

    /// <summary>경로·파일이 없다.</summary>
    NotFound,

    /// <summary>드라이브가 준비되지 않았다 (빈 카드 리더 등).</summary>
    DeviceNotReady,

    /// <summary>다른 프로세스가 잠갔다.</summary>
    Sharing,

    Unknown,
}

/// <summary>
/// 상태표시줄에 그대로 들어갈 한국어 문구 (docs/UI_GUIDE.md §상태 표현 —
/// 사유를 상태표시줄에 쓰고 경로는 그대로 둔다).
/// <para>
/// 분류와 문구를 한 파일에 둔다 — "오류가 무엇인가" 와 "그것을 어떻게 말하는가" 는
/// 같은 관심사다. 두 곳에서 만들면 같은 상황에 다른 말이 나간다.
/// </para>
/// </summary>
public static class LocationErrorMessages
{
    /// <summary>
    /// 경로는 <see cref="LocationId.Value"/> 가 아니라 <see cref="LocationId.DisplayPath"/> 를 쓴다 —
    /// 사용자가 <c>\\?\C:\Users</c> 를 보면 무슨 일인지 알 수 없다.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="kind"/> 가 <see cref="LocationErrorKind.None"/> 일 때.
    /// 오류가 아닌 것을 설명하라는 호출은 버그다.
    /// </exception>
    public static string Describe(LocationErrorKind kind, LocationId location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (kind == LocationErrorKind.None)
        {
            throw new ArgumentException("오류가 아닌 것을 설명할 수 없다.", nameof(kind));
        }

        var wording = kind switch
        {
            LocationErrorKind.AccessDenied => "액세스가 거부되었습니다",
            LocationErrorKind.NotFound => "경로를 찾을 수 없습니다",
            LocationErrorKind.DeviceNotReady => "드라이브가 준비되지 않았습니다",
            LocationErrorKind.Sharing => "다른 프로그램이 사용 중입니다",
            LocationErrorKind.Unknown => "열 수 없습니다",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "알 수 없는 분류다."),
        };

        return $"{wording} — {location.DisplayPath}";
    }
}
