using System.Globalization;

using FlexDir.Core.Formatting;

using Xunit;

namespace FlexDir.Core.Tests.Formatting;

/// <summary>
/// 수정한 날짜 컬럼 문자열 (docs/DESIGN.md §3 — 140px, §2 행 높이 24).
/// 표준 시간대와 culture 를 전부 인자로 받는지 확인한다 — 실행 기계의
/// <c>TimeZoneInfo.Local</c>·<c>CultureInfo.CurrentCulture</c> 를 읽으면 CI 와
/// 개발 기계에서 다른 문자열이 난다.
/// </summary>
public class TimestampFormatterTests
{
    /// <summary>초(47)가 결과에 남는지 확인할 수 있게 다른 자리와 겹치지 않는 값을 골랐다.</summary>
    private static readonly DateTimeOffset Utc = new(2026, 8, 3, 15, 30, 47, TimeSpan.Zero);

    // ── 시간대를 인자로 받는다 ──────────────────────────────────────

    [Fact]
    public void DifferentZones_YieldDifferentTimes()
    {
        var seoul = TimestampFormatter.Format(Utc, FixedOffset("+09", 9), CultureInfo.InvariantCulture);
        var newYork = TimestampFormatter.Format(Utc, FixedOffset("-05", -5), CultureInfo.InvariantCulture);

        Assert.NotEqual(seoul, newYork);
    }

    [Fact]
    public void Zone_ShiftsAcrossDayBoundary()
    {
        // 15:30 UTC + 9h = 다음 날 00:30. ToLocalTime() 을 쓰면 이 값이 기계마다 달라진다.
        Assert.Equal(
            "08/04/2026 00:30",
            TimestampFormatter.Format(Utc, FixedOffset("+09", 9), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Zone_BehindUtc_StaysOnPreviousDay()
    {
        Assert.Equal(
            "08/03/2026 10:30",
            TimestampFormatter.Format(Utc, FixedOffset("-05", -5), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UtcZone_KeepsTheSameInstant()
    {
        Assert.Equal(
            "08/03/2026 15:30",
            TimestampFormatter.Format(Utc, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));
    }

    // ── 짧은 날짜 + 짧은 시간. 초는 없다 ────────────────────────────
    // 초까지 나오면 140px 컬럼과 24px 행에서 잘린다 (docs/DESIGN.md §2·§3).

    [Theory]
    [InlineData("")]
    [InlineData("ko-KR")]
    public void Seconds_AreNotIncluded(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var formatted = TimestampFormatter.Format(Utc, FixedOffset("+09", 9), culture);

        Assert.DoesNotContain("47", formatted);
    }

    // ── 로케일 패턴을 쓴다 ──────────────────────────────────────────

    [Fact]
    public void Culture_ChangesThePattern()
    {
        var invariant = TimestampFormatter.Format(Utc, FixedOffset("+09", 9), CultureInfo.InvariantCulture);
        var korean = TimestampFormatter.Format(Utc, FixedOffset("+09", 9), new CultureInfo("ko-KR"));

        Assert.NotEqual(invariant, korean);
    }

    [Fact]
    public void Format_JoinsShortDateAndShortTimePatterns()
    {
        var ko = new CultureInfo("ko-KR");
        var local = TimeZoneInfo.ConvertTime(Utc, FixedOffset("+09", 9));
        var expected = local.ToString(ko.DateTimeFormat.ShortDatePattern, ko)
            + " "
            + local.ToString(ko.DateTimeFormat.ShortTimePattern, ko);

        Assert.Equal(expected, TimestampFormatter.Format(Utc, FixedOffset("+09", 9), ko));
    }

    [Fact]
    public void NullCulture_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimestampFormatter.Format(Utc, TimeZoneInfo.Utc, null!));
    }

    [Fact]
    public void NullZone_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimestampFormatter.Format(Utc, null!, CultureInfo.InvariantCulture));
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    /// <summary>
    /// 시스템 시간대 ID 는 플랫폼마다 다르고("Korea Standard Time" vs "Asia/Seoul")
    /// DST 규칙도 바뀐다. 고정 오프셋 시간대를 직접 만들어 결과를 못 박는다.
    /// </summary>
    private static TimeZoneInfo FixedOffset(string id, int hours)
        => TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(hours), id, id);
}
