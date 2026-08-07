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

    // ── 자격증명 충돌 (docs/PRD-v2.md §5 N-4) ───────────────────────
    // 1219 는 비밀번호나 주소를 고쳐도 안 되는 종류다. 무엇을 해야 하는지 말하지 않으면
    // 사용자가 손쓸 방법이 없다 — 그래서 이 분류만 문구에 명령을 담는다.

    [Fact]
    public void Describe_CredentialConflict_NamesTheCommandThatFixesIt()
    {
        Assert.Equal(
            @"다른 자격 증명으로 이미 연결돼 있습니다 — net use \\10.10.10.23 /delete 후 다시 여세요",
            LocationErrorMessages.Describe(LocationErrorKind.CredentialConflict, Parse(@"\\10.10.10.23")));
    }

    [Fact]
    public void Describe_CredentialConflict_TargetsTheServerNotTheShare()
    {
        // 세션은 서버 단위로 하나다. `net use \\server\share\sub /delete` 는 실패한다 —
        // 붙여 넣어서 안 되는 명령을 안내하면 없느니만 못하다.
        Assert.Equal(
            @"다른 자격 증명으로 이미 연결돼 있습니다 — net use \\10.10.10.23 /delete 후 다시 여세요",
            LocationErrorMessages.Describe(LocationErrorKind.CredentialConflict, Parse(@"\\10.10.10.23\home\sub")));
    }

    [Fact]
    public void Describe_CredentialConflict_OnALocalPath_FallsBackToThePlainShape()
    {
        // 로컬 경로에는 끊을 세션이 없다. 여기까지 오는 것은 호출자 버그이지만, 그때
        // `net use  /delete` 라고 말하는 것보다 사유만 말하는 편이 낫다.
        Assert.Equal(
            @"다른 자격 증명으로 이미 연결돼 있습니다 — C:\Temp",
            LocationErrorMessages.Describe(LocationErrorKind.CredentialConflict, Parse(@"C:\Temp")));
    }

    [Fact]
    public void Describe_CredentialConflict_IsNotTheAccessDeniedWording()
    {
        // AccessDenied 로 접으면 "비밀번호를 고치면 되겠지" 로 읽힌다. 1219 는 그래도 안 된다.
        var location = Parse(@"\\10.10.10.23");

        Assert.NotEqual(
            LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, location),
            LocationErrorMessages.Describe(LocationErrorKind.CredentialConflict, location));
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
                     LocationErrorKind.CredentialConflict,
                     LocationErrorKind.Unknown,
                 })
        {
            var message = LocationErrorMessages.Describe(kind, location);

            Assert.DoesNotContain(@"\\?\", message);
            Assert.Contains(location.DisplayPath, message);
        }
    }

    [Fact]
    public void Describe_CredentialConflict_UsesTheDisplayFormOfTheServer()
    {
        // 명령에 들어가는 것도 사용자가 그대로 치는 문자열이다. 내부 표현이 새면 못 친다.
        var message = LocationErrorMessages.Describe(
            LocationErrorKind.CredentialConflict, Parse(@"\\10.10.10.23\home"));

        Assert.DoesNotContain(@"\\?\", message);
        Assert.Contains(@"\\10.10.10.23", message);
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
            LocationErrorKind.CredentialConflict,
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
                     LocationErrorKind.CredentialConflict,
                     LocationErrorKind.Unknown,
                 })
        {
            var message = LocationErrorMessages.Describe(kind, location);
            var wording = message[..message.IndexOf(location.DisplayPath, StringComparison.Ordinal)];

            Assert.False(wording.Any(char.IsAsciiLetter), wording);
        }
    }

    [Fact]
    public void Describe_TheOnlyEnglishIsACommandToType()
    {
        // 위 규칙의 유일한 예외다. 명령은 산문이 아니라 <b>사용자가 그대로 치는 원문</b>이라
        // 경로와 같은 취급이다 — 번역하면 붙여 넣어서 안 되는 명령이 된다.
        // 예외가 이 하나뿐임을 여기서 고정한다: 그것을 빼면 한국어만 남아야 한다.
        var message = LocationErrorMessages.Describe(
            LocationErrorKind.CredentialConflict, Parse(@"\\10.10.10.23"));

        Assert.Contains(@"net use \\10.10.10.23 /delete", message);

        var withoutTheCommand = message.Replace(@"net use \\10.10.10.23 /delete", string.Empty);

        Assert.False(withoutTheCommand.Any(char.IsAsciiLetter), withoutTheCommand);
    }
}
