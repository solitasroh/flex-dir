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
        Assert.Equal(@"\\?\UNC\server\share", Parse("//server/share").Value);
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
        var location = Parse(@"\\?\UNC\server\share");

        Assert.Equal(@"\\?\UNC\server\share", location.Value);
        Assert.Equal(@"\\server\share", location.DisplayPath);
    }

    // ── 함정 4: \\server (공유 없는 서버) 도 유효한 탐색 대상 ───────
    // 공유 목록을 여는 자리다 (docs/PRD-v2.md §5 N-3). InvalidCharacter·RelativePath 로
    // 뭉개면 그 분기를 만들 수 없다.

    [Fact]
    public void TryParse_ServerOnlyUnc_IsNetworkNotInvalid()
    {
        var location = Parse(@"\\server");

        Assert.Equal(@"\\?\UNC\server", location.Value);
        Assert.Equal(@"\\server", location.DisplayPath);
    }

    // ── UNC (docs/PRD-v2.md §5 N-1) ───────────────────────────────
    // 내부 표현은 Win32 확장 UNC 형식 \\?\UNC\server\share 다. 보이는 형태는 \\server\share.
    // 드라이브 경로와 같은 타입에 담는다 (ADR-010) — 포트가 둘로 갈리지 않는다.

    [Fact]
    public void Unc_IsFileSystemKind()
    {
        // SMB 리다이렉터를 지나는 파일시스템 경로다. 셸 네임스페이스가 아니다.
        Assert.Equal(LocationKind.FileSystem, Parse(@"\\10.10.10.23\공유").Kind);
    }

    [Theory]
    [InlineData(@"\\server\share\a\b", @"\\?\UNC\server\share\a\b")]
    [InlineData(@"\\server\share\", @"\\?\UNC\server\share")]
    [InlineData(@"\\server\share\a\..\b", @"\\?\UNC\server\share\b")]
    [InlineData(@"\\server\share\.\a", @"\\?\UNC\server\share\a")]
    [InlineData(@"\\server\\share", @"\\?\UNC\server\share")]
    [InlineData(@"//server/share/a", @"\\?\UNC\server\share\a")]
    [InlineData(@"\\wsl$\Ubuntu\home", @"\\?\UNC\wsl$\Ubuntu\home")]
    public void Unc_IsNormalizedLikeALocalPath(string input, string expected)
    {
        Assert.Equal(expected, Parse(input).Value);
    }

    // 루트를 넘어가는 '..' 는 서버에서 멈춘다 — 드라이브 루트와 같은 규칙이다.
    [Fact]
    public void Unc_DotDot_StopsAtTheServer()
    {
        Assert.Equal(@"\\?\UNC\server", Parse(@"\\server\share\..\..\..").Value);
    }

    [Theory]
    [InlineData(@"\\server\share\a", "a")]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"\\server", @"\\server")]      // 루트는 자기 표시형이 이름이다 (C:\ 와 같다)
    public void Unc_NameIsTheLastComponent(string input, string expected)
    {
        Assert.Equal(expected, Parse(input).Name);
    }

    [Theory]
    [InlineData(@"\\server\share\a\b", @"\\server\share\a")]
    [InlineData(@"\\server\share\a", @"\\server\share")]
    [InlineData(@"\\server\share", @"\\server")]
    public void Unc_ParentGoesUpOneLevel(string input, string expected)
    {
        Assert.True(Parse(input).TryGetParent(out var parent));
        Assert.Equal(expected, parent.DisplayPath);
    }

    // 서버가 루트다. 여기서 더 올라가면 "네트워크" 노드인데 그것은 셸 네임스페이스라
    // v2 범위 밖이다 (ADR-014 — PIDL 경로는 따로 세운다).
    [Fact]
    public void Unc_ServerHasNoParent()
    {
        Assert.False(Parse(@"\\server").TryGetParent(out _));
    }

    [Fact]
    public void Unc_CombineAppendsUnderTheShare()
    {
        Assert.Equal(@"\\?\UNC\server\share\a", Parse(@"\\server\share").Combine("a").Value);
    }

    [Fact]
    public void Unc_CombineOnTheServerMakesAShare()
    {
        Assert.Equal(@"\\?\UNC\server\share", Parse(@"\\server").Combine("share").Value);
    }

    // 규칙 7 은 UNC 에도 그대로다 — 서버·공유 이름도 대소문자를 구분하지 않는다.
    [Fact]
    public void Unc_ComparesCaseInsensitively()
    {
        Assert.Equal(Parse(@"\\Server\Share\A"), Parse(@"\\server\share\a"));
    }

    [Fact]
    public void Unc_AndLocalPath_AreNeverEqual()
    {
        Assert.NotEqual(Parse(@"\\server\share"), Parse(@"C:\server\share"));
    }

    // 서버 이름이 없으면 갈 곳이 없다. RelativePath 로 뭉개면 사용자가 무엇을 고쳐야
    // 하는지 알 수 없다 — 오타인지 미지원인지가 갈린다.
    [Theory]
    [InlineData(@"\\")]
    [InlineData(@"\\\")]
    [InlineData(@"//")]
    [InlineData(@"\\?\UNC")]
    [InlineData(@"\\?\UNC\")]
    public void Unc_WithoutAServerName_IsIncomplete(string input)
    {
        Assert.Equal(LocationParseError.NetworkPathIncomplete, ParseError(input));
    }

    // 공유 목록을 열 자리인지 판정한다 (docs/PRD-v2.md §5 N-3). 열거 방식이 완전히
    // 다르므로(WNetEnumResource vs FileSystemEnumerator) 라우팅이 이것을 본다.
    [Theory]
    [InlineData(@"\\server", true)]
    [InlineData(@"\\10.10.10.23", true)]
    [InlineData(@"\\server\share", false)]
    [InlineData(@"\\server\share\a", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Temp", false)]
    public void IsNetworkServer_IsTrueOnlyForAServerRoot(string input, bool expected)
    {
        Assert.Equal(expected, Parse(input).IsNetworkServer);
    }

    // 자격증명 충돌(1219)은 공유가 아니라 <b>서버</b> 단위다 — 안내 문구가 끊으라고
    // 말할 대상이 이것이다 (docs/PRD-v2.md §5 N-4). 경로 표기를 아는 곳은 여기뿐이므로
    // 문구를 만드는 쪽이 문자열을 자르지 않게 한다.
    [Theory]
    [InlineData(@"\\server", @"\\server")]
    [InlineData(@"\\10.10.10.23", @"\\10.10.10.23")]
    [InlineData(@"\\10.10.10.23\home", @"\\10.10.10.23")]
    [InlineData(@"\\server\share\a\b", @"\\server")]
    public void Server_IsTheServerPartOfAUncPath(string input, string expected)
    {
        Assert.Equal(expected, Parse(input).Server);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Temp\a.txt")]
    public void Server_IsNullForALocalPath(string input)
    {
        // 로컬 경로에는 끊을 연결이 없다. 빈 문자열로 내면 문구가 `net use  /delete` 가 된다.
        Assert.Null(Parse(input).Server);
    }

    [Fact]
    public void Unc_WithAnInvalidCharacter_IsRejectedLikeALocalPath()
    {
        Assert.Equal(LocationParseError.InvalidCharacter, ParseError(@"\\server\sh|are"));
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

    // ── TryCombine — 이름이 거부된 사유 ────────────────────────────
    //
    // Combine 은 던진다. 사용자가 친 이름에 그것을 쓰면 예외가 커맨드 밖으로 새어
    // <b>프로세스가 죽는다</b> — 실물에서 그렇게 죽었다 (2026-08-07, 이름에 '\' 가 든 경우).
    // 사유를 값으로 받아야 화면에 무엇이 잘못됐는지 말할 수 있다. TryParse 와 같은 짝이다.

    [Theory]
    [InlineData("b.txt")]
    [InlineData("보고서 2026.xlsx")]
    [InlineData("a b")]
    public void TryCombine_AcceptsAUsableName(string name)
    {
        Assert.True(Parse(@"C:\Temp").TryCombine(name, out var child, out var error));
        Assert.Equal(ChildNameError.None, error);
        Assert.Equal($@"C:\Temp\{name}", child!.DisplayPath);
    }

    [Theory]
    [InlineData("", ChildNameError.Empty)]
    [InlineData("   ", ChildNameError.Empty)]
    [InlineData(null, ChildNameError.Empty)]
    [InlineData(@"a\b.txt", ChildNameError.Separator)]
    [InlineData("a/b.txt", ChildNameError.Separator)]
    [InlineData(".", ChildNameError.RelativeElement)]
    [InlineData("..", ChildNameError.RelativeElement)]
    [InlineData("회의록 10:30.txt", ChildNameError.InvalidCharacter)]
    [InlineData("무엇?.txt", ChildNameError.InvalidCharacter)]
    [InlineData("a|b.txt", ChildNameError.InvalidCharacter)]
    public void TryCombine_RejectsWithAReason(string? name, ChildNameError expected)
    {
        Assert.False(Parse(@"C:\Temp").TryCombine(name!, out var child, out var error));
        Assert.Equal(expected, error);
        Assert.Null(child);
    }

    [Fact]
    public void TryCombine_AndCombine_AgreeOnWhatIsAllowed()
    {
        // 규칙이 두 벌이 되면 한쪽만 고쳐지고 조용히 갈라진다. Combine 이 TryCombine 을 쓴다.
        foreach (var name in new[] { "b.txt", "", " ", @"a\b", "a/b", ".", "..", "a:b", "a*b" })
        {
            var folder = Parse(@"C:\Temp");
            var accepted = folder.TryCombine(name, out _, out _);

            if (accepted)
            {
                Assert.Equal(folder.Combine(name).DisplayPath, $@"C:\Temp\{name}");
            }
            else
            {
                Assert.Throws<ArgumentException>(() => folder.Combine(name));
            }
        }
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
