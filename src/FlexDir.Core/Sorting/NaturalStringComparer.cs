namespace FlexDir.Core.Sorting;

/// <summary>
/// 숫자 런을 수치로 비교하는 문자열 비교기. 대소문자를 구분하지 않는다.
/// <para>
/// shlwapi 의 <c>StrCmpLogicalW</c> 를 부르지 않는다 — <c>FlexDir.Core</c> 는 Windows API 를
/// 참조하지 않는다 (CLAUDE.md §1). 탐색기와 미세한 순서 차이가 남는 것은 알려진
/// 트레이드오프이며, <c>NaturalStringComparerTests</c> 가 우리 순서를 고정한다.
/// </para>
/// <para>
/// <see cref="System.Globalization.CompareInfo"/> 기반 언어별 정렬도 쓰지 않는다.
/// 로케일이 바뀌면 순서가 바뀌고, 폴더별로 기억한 정렬 상태가 다른 결과를 낸다.
/// </para>
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xi = 0;
        var yi = 0;

        // 수치는 같은데 선행 0 개수만 다른 첫 지점 (img1 vs img001).
        // 마지막 수단으로만 쓴다 — 뒤에 실제로 다른 내용이 있으면 그쪽이 이긴다.
        var zeroPadding = 0;

        while (xi < x.Length && yi < y.Length)
        {
            int compared;

            if (char.IsAsciiDigit(x[xi]) && char.IsAsciiDigit(y[yi]))
            {
                var xLength = DigitRunLength(x, xi);
                var yLength = DigitRunLength(y, yi);

                compared = CompareNumeric(x.AsSpan(xi, xLength), y.AsSpan(yi, yLength), ref zeroPadding);
                xi += xLength;
                yi += yLength;
            }
            else
            {
                // 한쪽만 숫자면 그쪽 문자 런은 비어 있다 — 숫자가 문자보다 앞선다.
                var xLength = TextRunLength(x, xi);
                var yLength = TextRunLength(y, yi);

                compared = x.AsSpan(xi, xLength).CompareTo(y.AsSpan(yi, yLength), StringComparison.OrdinalIgnoreCase);
                xi += xLength;
                yi += yLength;
            }

            if (compared != 0)
            {
                return compared;
            }
        }

        // 한쪽이 먼저 끝났으면 짧은 쪽이 앞이다 (file < file2).
        if (xi < x.Length)
        {
            return 1;
        }

        if (yi < y.Length)
        {
            return -1;
        }

        if (zeroPadding != 0)
        {
            return zeroPadding;
        }

        // 대소문자만 다른 이름을 동률로 두면 정렬이 불안정해진다 (readme vs README).
        return string.CompareOrdinal(x, y);
    }

    /// <summary>ASCII 숫자만 숫자로 본다. 다른 문자 체계의 숫자는 문자 런으로 흘려보낸다.</summary>
    private static int DigitRunLength(string text, int start)
    {
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end - start;
    }

    private static int TextRunLength(string text, int start)
    {
        var end = start;
        while (end < text.Length && !char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end - start;
    }

    /// <summary>
    /// 숫자 런을 수치로 비교한다. <c>long.Parse</c> 에 맡기지 않는다 — 파일명에 20자리
    /// 숫자가 오면 예외가 난다. 선행 0 을 떼고 길이 → 자릿값 순으로 본다.
    /// </summary>
    private static int CompareNumeric(ReadOnlySpan<char> x, ReadOnlySpan<char> y, ref int zeroPadding)
    {
        var xDigits = x.TrimStart('0');
        var yDigits = y.TrimStart('0');

        if (xDigits.Length != yDigits.Length)
        {
            return xDigits.Length < yDigits.Length ? -1 : 1;
        }

        // ASCII 숫자는 문자 순서와 자릿값 순서가 일치한다.
        var compared = xDigits.SequenceCompareTo(yDigits);
        if (compared != 0)
        {
            return compared;
        }

        // 수치는 같고 표기만 다르다. 값이 같으니 길이 차이가 곧 선행 0 개수 차이다.
        if (zeroPadding == 0)
        {
            zeroPadding = x.Length.CompareTo(y.Length);
        }

        return 0;
    }
}
