using System.Globalization;

namespace FlexDir.Core.Formatting;

/// <summary>
/// 수정한 날짜 컬럼 문자열 (docs/DESIGN.md §3 — 140px).
/// <para>
/// 표준 시간대와 culture 를 <b>전부 인자로 받는다.</b>
/// <see cref="TimeZoneInfo.Local"/>·<see cref="DateTimeOffset.ToLocalTime"/>·
/// <see cref="CultureInfo.CurrentCulture"/> 를 함수 안에서 읽으면 테스트가 실행 기계에
/// 묶이고, CI 와 개발 기계가 다른 결과를 낸다.
/// </para>
/// </summary>
public static class TimestampFormatter
{
    /// <summary>
    /// 짧은 날짜 + 짧은 시간. 로케일의 <c>ShortDatePattern</c> 과 <c>ShortTimePattern</c> 을
    /// 공백으로 잇는다. <c>"g"</c> 서식 지정자에 맡기지 않는다 — 초·오전/오후가 로케일마다
    /// 들쭉날쭉해지면 24px 행과 140px 컬럼 안에서 잘린다 (docs/DESIGN.md §2·§3).
    /// </summary>
    public static string Format(DateTimeOffset utc, TimeZoneInfo zone, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(zone);

        // null 이면 DateTimeFormatInfo.GetInstance 가 CurrentInfo 로 넘어간다.
        ArgumentNullException.ThrowIfNull(culture);

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var format = DateTimeFormatInfo.GetInstance(culture);

        return local.ToString(format.ShortDatePattern, culture)
            + " "
            + local.ToString(format.ShortTimePattern, culture);
    }
}
