using System.Globalization;

using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.Core.Tests.Formatting;

/// <summary>
/// 크기 컬럼 문자열 (docs/DESIGN.md §3 — 크기 컬럼 90px, 오른쪽 정렬).
/// 단위는 1024 기반이다. 탐색기와 나란히 놓고 값이 달라 보이면 안 된다.
/// 여기 고정한 표가 스펙이며, 실물 비교는 수동 UI phase 의 일이다.
/// </summary>
public class SizeFormatterTests
{
    // ── KB: 0 KB 로 표시하지 않는다 (올림) ──────────────────────────
    // 1바이트 파일이 '0 KB' 로 보이면 빈 파일과 구별되지 않는다.

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(1L, "1 KB")]
    [InlineData(1023L, "1 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1025L, "2 KB")]
    [InlineData(1048575L, "1,024 KB")]
    public void Kilobytes_RoundUp(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    // ── MB·GB: 소수 1자리 ──────────────────────────────────────────

    [Theory]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(1073741823L, "1,024.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    [InlineData(1610612736L, "1.5 GB")]
    public void MegabytesAndGigabytes_HaveOneDecimal(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Megabytes_Round_NotCeil()
    {
        // KB 의 올림 규칙을 소수 자리에 그대로 적용하면 1바이트 초과가 '1.1 MB' 로 튄다.
        Assert.Equal("1.0 MB", SizeFormatter.Format(1048577, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MaxValue_DoesNotOverflow()
    {
        // double 로 계산하면 자릿수를 잃는다. decimal 로 나눈다.
        Assert.EndsWith(" GB", SizeFormatter.Format(long.MaxValue, CultureInfo.InvariantCulture));
    }

    // ── 음수는 계산 실패의 신호다 ───────────────────────────────────

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void NegativeBytes_Throw(long bytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    // ── 천단위 구분 기호는 culture 에서 온다 ────────────────────────

    [Fact]
    public void ThousandsSeparator_AppearsInInvariantCulture()
    {
        Assert.Contains(",", SizeFormatter.Format(1048575, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ThousandsSeparator_AppearsInKorean()
    {
        var ko = new CultureInfo("ko-KR");

        Assert.Equal("1,024 KB", SizeFormatter.Format(1048575, ko));
        Assert.Equal("1,024.0 MB", SizeFormatter.Format(1073741823, ko));
    }

    [Fact]
    public void NullCulture_Throws()
    {
        // 조용히 CultureInfo.CurrentCulture 로 넘어가면 기계마다 다른 문자열이 난다.
        Assert.Throws<ArgumentNullException>(() => SizeFormatter.Format(1024, null!));
    }

    // ── ForItem: 디렉터리는 크기를 내지 않는다 ──────────────────────

    [Fact]
    public void ForItem_Directory_IsEmpty()
    {
        Assert.Equal(string.Empty, SizeFormatter.ForItem(Dir("Photos"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForItem_Directory_IgnoresSize()
    {
        // 열거 계층이 폴더에 0 이 아닌 크기를 채워 넣어도 컬럼은 비어 있어야 한다.
        // 폴더 용량 계산은 v1 범위 밖이다 (docs/PRD.md §3).
        Assert.Equal(string.Empty, SizeFormatter.ForItem(Dir("Photos", 4096), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForItem_File_MatchesFormat()
    {
        Assert.Equal("1.5 MB", SizeFormatter.ForItem(File("a.bin", 1572864), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ForItem_EmptyFile_IsZeroKilobytes()
    {
        // 빈 파일은 빈 문자열이 아니다. 폴더와 구별되어야 한다.
        Assert.Equal("0 KB", SizeFormatter.ForItem(File("empty.txt"), CultureInfo.InvariantCulture));
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private static FileItem File(string name, long size = 0)
        => Make(name, size, FileItemFlags.None);

    private static FileItem Dir(string name, long size = 0)
        => Make(name, size, FileItemFlags.Directory);

    private static FileItem Make(string name, long size, FileItemFlags flags)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var parent, out var error), $"파싱 실패: {error}");
        return new FileItem(name, parent.Combine(name), size, default, flags);
    }
}
