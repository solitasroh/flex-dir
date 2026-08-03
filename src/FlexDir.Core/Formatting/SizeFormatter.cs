using FlexDir.Core.Model;

namespace FlexDir.Core.Formatting;

/// <summary>
/// 크기 컬럼 문자열 (docs/DESIGN.md §3 — 90px, 오른쪽 정렬).
/// 단위는 <b>1024 기반</b>이다. SI(1000) 로 계산하면 탐색기와 나란히 놓았을 때 같은
/// 파일이 다른 크기로 보인다.
/// <para>
/// 주변 환경을 읽지 않는다 — culture 는 전부 인자로 받는다. 함수 안에서
/// <see cref="System.Globalization.CultureInfo.CurrentCulture"/> 를 읽으면 같은 코드가
/// 기계마다 다른 문자열을 낸다.
/// </para>
/// </summary>
public static class SizeFormatter
{
    private const long Kilobyte = 1024L;
    private const long Megabyte = Kilobyte * 1024L;
    private const long Gigabyte = Megabyte * 1024L;

    /// <param name="bytes">음수는 계산 실패의 신호다 — <see cref="ArgumentOutOfRangeException"/>.</param>
    public static string Format(long bytes, IFormatProvider culture)
    {
        // null 을 그냥 넘기면 ToString 이 CurrentCulture 로 넘어가 조용히 로케일에 묶인다.
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < Megabyte)
        {
            // 올림. 1바이트 파일이 '0 KB' 로 보이면 빈 파일과 구별되지 않는다.
            // bytes < Megabyte 이므로 더하기에서 넘칠 일은 없다.
            var kilobytes = (bytes + Kilobyte - 1) / Kilobyte;
            return kilobytes.ToString("N0", culture) + " KB";
        }

        var (unit, suffix) = bytes < Gigabyte ? (Megabyte, " MB") : (Gigabyte, " GB");

        // decimal 로 나눈다. double 은 long.MaxValue 근처에서 자릿수를 잃는다.
        // 소수 자리는 올림이 아니라 반올림이다 — 1바이트 초과가 '1.1 MB' 로 튀면 안 된다.
        var value = Math.Round((decimal)bytes / unit, 1, MidpointRounding.AwayFromZero);
        return value.ToString("N1", culture) + suffix;
    }

    /// <summary>
    /// 디렉터리는 빈 문자열을 낸다. 폴더 용량 계산은 v1 범위 밖이며 (docs/PRD.md §3),
    /// 열거 계층이 채워 넣은 <see cref="FileItem.Size"/> 값이 무엇이든 무시한다.
    /// </summary>
    public static string ForItem(FileItem item, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.IsDirectory ? string.Empty : Format(item.Size, culture);
    }
}
