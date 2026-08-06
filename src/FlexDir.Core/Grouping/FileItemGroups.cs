using FlexDir.Core.Model;
using FlexDir.Core.Sorting;

namespace FlexDir.Core.Grouping;

/// <summary>
/// Details 그룹화의 라벨 규칙 (docs/PRD-v2.md §6-1).
/// <para>
/// <b>그룹 경계는 정렬된 목록의 인접 비교로만 잡는다.</b> 그룹마다 순서 번호를 따로 두지
/// 않는 이유는, 그 번호가 <see cref="FileItemComparer"/> 의 순서와 어긋나는 순간 같은
/// 라벨의 헤더가 목록의 두 자리에 생기기 때문이다. 대신 <see cref="WithGroupKey"/> 로
/// 그룹 키를 정렬 1차 키로 앞세워 <b>같은 그룹이 반드시 붙어 있게</b> 만든다.
/// </para>
/// <para>
/// 표준 시간대는 <see cref="Formatting.TimestampFormatter"/> 와 같은 규약으로 인자다 —
/// 함수 안에서 <see cref="TimeZoneInfo.Local"/> 을 읽으면 테스트가 실행 기계에 묶인다.
/// </para>
/// </summary>
public static class FileItemGroups
{
    /// <summary>
    /// 폴더는 기준과 무관하게 이 그룹 하나다. <see cref="FileItemComparer"/> 가 폴더를 항상
    /// 위로 올리므로(directoriesFirst), 폴더를 기준별로 나누면 같은 라벨이 폴더 구간과
    /// 파일 구간에 두 번 나온다.
    /// </summary>
    public const string Folders = "폴더";

    /// <summary>
    /// 초성 19개를 기본 자음으로 접은 표. 쌍자음을 따로 두지 않는 이유는 정렬이 가~까를
    /// 한 덩이로 놓기 때문이다 — 나누면 ㄱ 과 ㄲ 헤더가 번갈아 나온다.
    /// </summary>
    private const string LeadingJamo = "ㄱㄱㄴㄷㄷㄹㅁㅂㅂㅅㅅㅇㅈㅈㅊㅋㅌㅍㅎ";

    private const char HangulBase = '가';
    private const char HangulLast = '힣';
    private const int SyllablesPerJamo = 588;

    private const long Kilobyte = 1024L;
    private const long Megabyte = 1024L * 1024L;

    /// <summary>
    /// 항목이 속한 그룹의 표시 라벨.
    /// </summary>
    /// <param name="key">
    /// 그룹 기준. 정렬 기준과 같은 열거형을 쓴다 — 그룹 키가 곧 정렬 1차 키이므로
    /// 둘이 갈리면 그룹 경계와 정렬 순서가 어긋난다.
    /// </param>
    /// <param name="now">"오늘" 의 기준 시각. <paramref name="zone"/> 으로 변환해 쓴다.</param>
    public static string LabelOf(FileItem item, SortKey key, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(zone);

        if (item.IsDirectory)
        {
            return Folders;
        }

        return key switch
        {
            SortKey.Name => NameLabel(item.Name),
            SortKey.Size => SizeLabel(item.Size),
            SortKey.Type => TypeLabel(item.Extension),
            SortKey.Modified => ModifiedLabel(item.ModifiedUtc, now, zone),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "알 수 없는 그룹 기준이다."),
        };
    }

    /// <summary>
    /// 그룹 키를 정렬 1차 키로 앞세운다. <paramref name="group"/> 이 <c>null</c> 이면
    /// 정렬을 그대로 돌려준다 — 그룹화가 꺼져 있으면 목록 경로가 v1 과 한 글자도 다르지 않다.
    /// <para>
    /// 이미 그 키로 정렬 중이면 <b>그 방향을 따른다.</b> 헤더의 방향 글리프와 그룹 순서가
    /// 어긋나면 사용자는 정렬이 안 먹었다고 읽는다.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SortOrder> WithGroupKey(IReadOnlyList<SortOrder> sort, SortKey? group)
    {
        ArgumentNullException.ThrowIfNull(sort);

        if (group is not { } key)
        {
            return sort;
        }

        var descending = false;

        foreach (var order in sort)
        {
            if (order.Key == key)
            {
                descending = order.Descending;
                break;
            }
        }

        return [new SortOrder(key, descending), .. sort];
    }

    /// <summary>
    /// 첫 글자. 기호를 "기타" 같은 한 라벨로 묶지 않는다 —
    /// <see cref="NaturalStringComparer"/> 는 텍스트 런을 <c>OrdinalIgnoreCase</c> 로 비교하므로
    /// <c>(</c> 는 숫자보다 앞이고 <c>_</c> 는 <c>Z</c> 보다 뒤다. 한 라벨로 묶는 순간
    /// 그 헤더가 목록의 두 자리에 생긴다.
    /// </summary>
    private static string NameLabel(string name)
    {
        var first = name[0];

        if (char.IsAsciiDigit(first))
        {
            return "0-9";
        }

        if (first is >= HangulBase and <= HangulLast)
        {
            return LeadingJamo[(first - HangulBase) / SyllablesPerJamo].ToString();
        }

        return char.ToUpperInvariant(first).ToString();
    }

    /// <summary>탐색기와 같은 구간. 단위는 1024 기반이다 (<see cref="Formatting.SizeFormatter"/>).</summary>
    private static string SizeLabel(long bytes) => bytes switch
    {
        <= 0 => "0 KB",
        < 10 * Kilobyte => "매우 작음 (0–10 KB)",
        < 100 * Kilobyte => "작음 (10–100 KB)",
        < Megabyte => "보통 (100 KB–1 MB)",
        < 16 * Megabyte => "큼 (1–16 MB)",
        < 128 * Megabyte => "매우 큼 (16–128 MB)",
        _ => "거대 (128 MB 이상)",
    };

    /// <summary>
    /// 확장자를 쓴다. shell 이 주는 유형 이름을 쓰지 않는 이유는 둘이다 —
    /// 정렬(<see cref="SortKey.Type"/>)이 확장자로 비교하므로 다른 값을 쓰면 그룹 경계가
    /// 정렬과 어긋나고, 유형 이름은 비동기로 도착해 그룹이 뒤늦게 흔들린다.
    /// </summary>
    private static string TypeLabel(string extension)
        => extension.Length == 0 ? "확장자 없음" : extension.ToUpperInvariant();

    /// <summary>
    /// 최근 구간부터 차례로 본다. 경계가 앞 경계보다 뒤면 그 구간은 그냥 비는데
    /// (8월 첫 주의 "이번 달" 이 그렇다) 순서는 그대로 단조라 헤더가 겹치지 않는다.
    /// </summary>
    private static string ModifiedLabel(DateTimeOffset modifiedUtc, DateTimeOffset now, TimeZoneInfo zone)
    {
        var modified = TimeZoneInfo.ConvertTime(modifiedUtc, zone).Date;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;

        // 월요일 시작. 미래(시계 어긋남·네트워크)는 오늘로 본다.
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        return modified switch
        {
            _ when modified >= today => "오늘",
            _ when modified >= today.AddDays(-1) => "어제",
            _ when modified >= thisWeek => "이번 주",
            _ when modified >= thisWeek.AddDays(-7) => "지난주",
            _ when modified >= new DateTime(today.Year, today.Month, 1) => "이번 달",
            _ when modified >= new DateTime(today.Year, today.Month, 1).AddMonths(-1) => "지난달",
            _ when modified >= new DateTime(today.Year, 1, 1) => "올해",
            _ => "오래 전",
        };
    }
}
