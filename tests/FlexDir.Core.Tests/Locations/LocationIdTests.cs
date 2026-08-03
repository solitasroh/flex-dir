using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.Core.Tests.Locations;

/// <summary>
/// 함정 번호는 docs/SHELL_NOTES.md §경로 정규화 를 가리킨다.
/// </summary>
public class LocationIdTests
{
    // ── 함정 1: UNC 판정이 구분자 정규화보다 먼저다 ────────────────
    // '/' 를 먼저 '\' 로 바꾸면 드라이브 문자가 없어 상대 경로로 오분류된다.

    [Fact]
    public void TryParse_ForwardSlashUnc_IsNetworkNotRelativePath()
    {
        Assert.Equal(LocationParseError.NetworkPathNotSupported, ParseError("//server/share"));
    }

    // ── 함정 2: 본문의 ':' 는 대체 데이터 스트림, 드라이브의 ':' 는 합법 ──

    [Fact]
    public void TryParse_AlternateDataStream_IsRejected()
    {
        Assert.Equal(LocationParseError.AlternateDataStream, ParseError(@"C:\a\b:stream"));
    }

    [Fact]
    public void TryParse_DriveColon_IsAccepted()
    {
        Assert.Equal(@"\\?\C:\a", Parse(@"C:\a").Value);
    }

    // ── 함정 3: \\?\C:\ 는 UNC 가 아니다 ──────────────────────────

    [Fact]
    public void TryParse_ExtendedPrefixedLocalPath_IsFileSystem()
    {
        var location = Parse(@"\\?\C:\Users");

        Assert.Equal(LocationKind.FileSystem, location.Kind);
        Assert.Equal(@"\\?\C:\Users", location.Value);
        Assert.Equal(@"C:\Users", location.DisplayPath);
    }

    [Fact]
    public void TryParse_ExtendedPrefixedUnc_IsNetwork()
    {
        Assert.Equal(LocationParseError.NetworkPathNotSupported, ParseError(@"\\?\UNC\server\share"));
    }

    // ── 함정 4: \\server (공유 없는 서버) 도 유효한 탐색 대상 ───────
    // v1 은 거부하지만 InvalidCharacter·RelativePath 로 뭉개면 v2 가 이 분기를 못 쓴다.

    [Fact]
    public void TryParse_ServerOnlyUnc_IsNetworkNotInvalid()
    {
        Assert.Equal(LocationParseError.NetworkPathNotSupported, ParseError(@"\\server"));
    }

    // ── 거부 분류 ─────────────────────────────────────────────────

    [Fact]
    public void TryParse_RelativePath_IsRejected()
    {
        Assert.Equal(LocationParseError.RelativePath, ParseError("docs"));
    }

    [Theory]
    [InlineData("C:")]        // 드라이브 상대 — 프로세스의 현재 디렉터리에 의존한다
    [InlineData(@"C:docs")]
    [InlineData(@"\docs")]    // 루트 상대 — 현재 드라이브에 의존한다
    public void TryParse_DriveRelativePath_IsRejected(string input)
    {
        Assert.Equal(LocationParseError.RelativePath, ParseError(input));
    }

    [Fact]
    public void TryParse_Empty_IsRejected()
    {
        Assert.Equal(LocationParseError.Empty, ParseError(""));
        Assert.Equal(LocationParseError.Empty, ParseError(null));
        Assert.Equal(LocationParseError.Empty, ParseError("   "));
    }

    [Theory]
    [InlineData(@"C:\a<b")]
    [InlineData(@"C:\a|b")]
    [InlineData(@"C:\a*b")]
    public void TryParse_InvalidCharacter_IsRejected(string input)
    {
        Assert.Equal(LocationParseError.InvalidCharacter, ParseError(input));
    }

    // ── 정규화 ────────────────────────────────────────────────────

    [Fact]
    public void TryParse_DotAndDotDot_AreResolved()
    {
        Assert.Equal(@"\\?\C:\a\c", Parse(@"C:\a\.\b\..\c").Value);
    }

    [Fact]
    public void TryParse_DotDotBeyondRoot_StopsAtRoot()
    {
        Assert.Equal(@"\\?\C:\", Parse(@"C:\a\..\..\..").Value);
    }

    [Fact]
    public void TryParse_TrailingSeparator_IsRemoved()
    {
        Assert.Equal(@"\\?\C:\Temp", Parse(@"C:\Temp\").Value);
    }

    [Fact]
    public void TryParse_DriveRoot_KeepsSeparator()
    {
        var root = Parse(@"C:\");

        Assert.Equal(@"\\?\C:\", root.Value);
        Assert.Equal(@"C:\", root.DisplayPath);
        Assert.Equal(@"C:\", root.Name);
    }

    [Fact]
    public void TryParse_ForwardSlashLocalPath_IsNormalized()
    {
        Assert.Equal(@"\\?\C:\a\b", Parse("C:/a/b").Value);
    }

    [Fact]
    public void Name_IsLastComponent()
    {
        Assert.Equal("b", Parse(@"C:\a\b").Name);
    }

    // ── 부모 ──────────────────────────────────────────────────────

    [Fact]
    public void TryGetParent_DriveRoot_HasNoParent()
    {
        Assert.False(Parse(@"C:\").TryGetParent(out var parent));
        Assert.Null(parent);
    }

    [Fact]
    public void TryGetParent_FirstLevel_IsDriveRoot()
    {
        Assert.True(Parse(@"C:\Temp").TryGetParent(out var parent));
        Assert.Equal(@"\\?\C:\", parent!.Value);
    }

    [Fact]
    public void TryGetParent_NestedPath_DropsLastComponent()
    {
        Assert.True(Parse(@"C:\Temp\a\b").TryGetParent(out var parent));
        Assert.Equal(@"\\?\C:\Temp\a", parent!.Value);
    }

    // ── 동등성 (규칙 7: OrdinalIgnoreCase) ────────────────────────

    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        var upper = Parse(@"\\?\C:\Temp");
        var lower = Parse(@"\\?\c:\temp");

        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentPaths_AreNotEqual()
    {
        Assert.NotEqual(Parse(@"C:\a"), Parse(@"C:\b"));
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        Assert.False(Parse(@"C:\a").Equals(null));
    }

    // ── Combine ───────────────────────────────────────────────────

    [Fact]
    public void Combine_ChildName_AppendsComponent()
    {
        Assert.Equal(@"\\?\C:\Temp\x", Parse(@"C:\Temp").Combine("x").Value);
    }

    [Fact]
    public void Combine_OnDriveRoot_DoesNotDoubleSeparator()
    {
        Assert.Equal(@"\\?\C:\x", Parse(@"C:\").Combine("x").Value);
    }

    [Theory]
    [InlineData(@"a\b")]   // 구분자
    [InlineData("a/b")]
    [InlineData("..")]     // 상대경로 요소
    [InlineData(".")]
    [InlineData("")]
    [InlineData("a:b")]    // 대체 데이터 스트림 — 파싱할 수 없는 Value 가 만들어진다
    public void Combine_InvalidChildName_Throws(string childName)
    {
        var location = Parse(@"C:\Temp");

        Assert.Throws<ArgumentException>(() => location.Combine(childName));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out var error), $"파싱 실패: {error}");
        Assert.Equal(LocationParseError.None, error);
        return location!;
    }

    private static LocationParseError ParseError(string? input)
    {
        Assert.False(LocationId.TryParse(input, out var location, out var error));
        Assert.Null(location);
        return error;
    }
}
