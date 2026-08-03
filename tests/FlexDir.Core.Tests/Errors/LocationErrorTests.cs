using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.Core.Tests.Errors;

/// <summary>
/// 사용자에게 나가는 오류 문구 (docs/UI_GUIDE.md §상태 표현 — 권한 없음은 상태
/// 표시줄에 사유를 쓰고 경로는 그대로 둔다). 상태표시줄 한 줄이므로 한국어로만 쓴다.
/// </summary>
public class LocationErrorTests
{
    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out _));
        return location;
    }

    // ── 분류별 문구 ─────────────────────────────────────────────────

    [Fact]
    public void Describe_AccessDenied()
    {
        Assert.Equal(
            @"액세스가 거부되었습니다 — C:\Windows\System32\config",
            LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, Parse(@"C:\Windows\System32\config")));
    }

    [Fact]
    public void Describe_NotFound()
    {
        Assert.Equal(
            @"경로를 찾을 수 없습니다 — C:\Temp\gone",
            LocationErrorMessages.Describe(LocationErrorKind.NotFound, Parse(@"C:\Temp\gone")));
    }

    [Fact]
    public void Describe_DeviceNotReady()
    {
        Assert.Equal(
            @"드라이브가 준비되지 않았습니다 — E:\",
            LocationErrorMessages.Describe(LocationErrorKind.DeviceNotReady, Parse(@"E:\")));
    }

    [Fact]
    public void Describe_Sharing()
    {
        Assert.Equal(
            @"다른 프로그램이 사용 중입니다 — C:\Temp\report.xlsx",
            LocationErrorMessages.Describe(LocationErrorKind.Sharing, Parse(@"C:\Temp\report.xlsx")));
    }

    [Fact]
    public void Describe_Unknown()
    {
        Assert.Equal(
            @"열 수 없습니다 — C:\Temp",
            LocationErrorMessages.Describe(LocationErrorKind.Unknown, Parse(@"C:\Temp")));
    }

    // ── 경로 표기 ───────────────────────────────────────────────────

    [Fact]
    public void Describe_UsesDisplayPath_NotTheExtendedPrefix()
    {
        // 사용자가 \\?\C:\Users 를 보면 무슨 일인지 알 수 없다.
        var location = Parse(@"C:\Users");

        foreach (var kind in new[]
                 {
                     LocationErrorKind.AccessDenied,
                     LocationErrorKind.NotFound,
                     LocationErrorKind.DeviceNotReady,
                     LocationErrorKind.Sharing,
                     LocationErrorKind.Unknown,
                 })
        {
            var message = LocationErrorMessages.Describe(kind, location);

            Assert.DoesNotContain(@"\\?\", message);
            Assert.Contains(location.DisplayPath, message);
        }
    }

    [Fact]
    public void Describe_KeepsTheDisplayPathVerbatim()
    {
        // 경로는 기술 원문이다. 자르거나 다듬지 않는다.
        var location = Parse(@"C:\사진\2026 여름\a.jpg");

        Assert.EndsWith(@"C:\사진\2026 여름\a.jpg", LocationErrorMessages.Describe(LocationErrorKind.NotFound, location));
    }

    // ── 오류가 아닌 것 ──────────────────────────────────────────────

    [Fact]
    public void Describe_None_Throws()
    {
        // 오류가 아닌 것을 설명하라는 호출은 버그다. 빈 문자열로 삼키면 상태표시줄이 조용히 빈다.
        Assert.Throws<ArgumentException>(
            () => LocationErrorMessages.Describe(LocationErrorKind.None, Parse(@"C:\Temp")));
    }

    [Fact]
    public void Describe_NullLocation_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => LocationErrorMessages.Describe(LocationErrorKind.NotFound, null!));
    }

    [Fact]
    public void Describe_UnknownKindValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LocationErrorMessages.Describe((LocationErrorKind)99, Parse(@"C:\Temp")));
    }

    // ── 문구가 서로 구별된다 ────────────────────────────────────────

    [Fact]
    public void Describe_EveryKindHasItsOwnWording()
    {
        // 뭉뚱그리면 사용자가 손쓸 방법을 알 수 없다 (docs/SHELL_NOTES.md §오류 코드 매핑).
        var location = Parse(@"C:\Temp");

        var messages = new[]
        {
            LocationErrorKind.AccessDenied,
            LocationErrorKind.NotFound,
            LocationErrorKind.DeviceNotReady,
            LocationErrorKind.Sharing,
            LocationErrorKind.Unknown,
        }.Select(kind => LocationErrorMessages.Describe(kind, location)).ToArray();

        Assert.Equal(messages.Length, messages.Distinct().Count());
    }

    [Fact]
    public void Describe_IsKoreanOnly()
    {
        // 영어와 한국어를 섞으면 같은 오류를 다른 것으로 읽는다. 경로만 원문이다.
        var location = Parse(@"C:\Temp");

        foreach (var kind in new[]
                 {
                     LocationErrorKind.AccessDenied,
                     LocationErrorKind.NotFound,
                     LocationErrorKind.DeviceNotReady,
                     LocationErrorKind.Sharing,
                     LocationErrorKind.Unknown,
                 })
        {
            var message = LocationErrorMessages.Describe(kind, location);
            var wording = message[..message.IndexOf(location.DisplayPath, StringComparison.Ordinal)];

            Assert.False(wording.Any(char.IsAsciiLetter), wording);
        }
    }
}
