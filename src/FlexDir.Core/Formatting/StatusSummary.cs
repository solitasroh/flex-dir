namespace FlexDir.Core.Formatting;

/// <summary>
/// 상태표시줄 문자열 (docs/DESIGN.md §1 높이 24 · §4 폰트 11).
/// UI 텍스트는 한국어다. 구분자는 가운뎃점 하나로 통일한다 — 폭이 좁으므로 짧게 유지한다.
/// <para>
/// 오류 문구는 여기 만들지 않는다. 분류와 문구는 <c>error-classification</c> 의 책임이며,
/// 두 곳에서 만들면 같은 상황에 다른 말이 나간다.
/// </para>
/// </summary>
public static class StatusSummary
{
    private const string Separator = " · ";

    /// <summary>
    /// 예: <c>여유 공간 213.0 GB</c>. 상태표시줄 오른쪽 끝에 붙는다 (목업 <c>.free</c>).
    /// <para>
    /// 단위는 크기 컬럼과 같은 <see cref="SizeFormatter"/> 를 지난다 — 같은 창 안에서
    /// 1024 기반과 SI 가 섞이면 어느 쪽이 맞는지 알 수 없게 된다.
    /// </para>
    /// </summary>
    public static string ForFreeSpace(long freeBytes, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return $"여유 공간 {SizeFormatter.Format(freeBytes, culture)}";
    }

    /// <summary>예: <c>항목 232개</c>. 0 도 그대로 낸다 — 빈 폴더 문구를 고르는 것은 호출자다.</summary>
    public static string ForItems(int itemCount, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return $"항목 {Count(itemCount, culture)}개";
    }

    /// <summary>
    /// 예: <c>232개 중 3개 선택 · 1.2 MB</c>.
    /// <paramref name="selectedCount"/> 가 0 이면 <see cref="ForItems"/> 와 같은 문자열을 낸다 —
    /// 호출자가 분기하지 않아도 되게 한다.
    /// </summary>
    public static string ForSelection(int itemCount, int selectedCount, long selectedBytes, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (selectedCount == 0)
        {
            return ForItems(itemCount, culture);
        }

        var size = SizeFormatter.Format(selectedBytes, culture);

        return $"{Count(itemCount, culture)}개 중 {Count(selectedCount, culture)}개 선택{Separator}{size}";
    }

    /// <summary>
    /// 예: <c>항목 1,204개 읽는 중…</c>. 목록을 비우지 않고 점진적으로 채우므로
    /// (docs/UI_GUIDE.md §상태 표현) 지금 보이는 개수가 최종 개수가 아님을 알려야 한다.
    /// </summary>
    public static string ForEnumerating(int itemsSoFar, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return $"항목 {Count(itemsSoFar, culture)}개 읽는 중…";
    }

    /// <summary>빈 폴더 (docs/PRD.md §4 — 목록 영역 자체는 유지한다).</summary>
    public static string Empty => "빈 폴더";

    /// <summary>천단위 구분 기호는 culture 에서 온다.</summary>
    private static string Count(int value, IFormatProvider culture) => value.ToString("N0", culture);
}
