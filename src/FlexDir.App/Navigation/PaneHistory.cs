using FlexDir.Core.Locations;

namespace FlexDir.App.Navigation;

/// <summary>
/// 한 페인의 방문 기록. 브라우저 히스토리와 같은 규칙이다.
/// PaneViewModel 이 소유하는 내부 상태이며 View 가 직접 바인딩하지 않는다.
/// </summary>
public sealed class PaneHistory
{
    public const int MaxEntries = 100;

    private readonly List<LocationId> entries = [];

    /// <summary>현재 위치의 첨자. 아직 아무 곳도 방문하지 않았으면 -1.</summary>
    private int index = -1;

    /// <summary>아직 아무 곳도 방문하지 않았으면 null.</summary>
    public LocationId? Current => index < 0 ? null : entries[index];

    public bool CanGoBack => index > 0;

    public bool CanGoForward => index >= 0 && index < entries.Count - 1;

    /// <summary>새 위치로 이동한다. 앞으로 기록은 버려진다.</summary>
    public void Navigate(LocationId location)
    {
        // 새로 고침·같은 폴더 재선택으로 앞으로 기록을 잃으면 사용자가 놀란다.
        // 비교는 LocationId 동등성(OrdinalIgnoreCase)이 판정한다.
        if (location.Equals(Current))
        {
            return;
        }

        entries.RemoveRange(index + 1, entries.Count - index - 1);
        entries.Add(location);
        index = entries.Count - 1;

        // 넘치면 가장 오래된 것부터 버린다 — 그만큼 뒤로가 먼저 막힌다.
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(0, entries.Count - MaxEntries);
            index = entries.Count - 1;
        }
    }

    /// <summary>
    /// 이동 후의 위치. 갈 수 없으면 null 을 내고 상태를 바꾸지 않는다 —
    /// CanExecute 와 실행 사이의 경합이 ViewModel 을 오류 상태로 보내지 않게.
    /// </summary>
    public LocationId? GoBack() => CanGoBack ? entries[--index] : null;

    /// <inheritdoc cref="GoBack"/>
    public LocationId? GoForward() => CanGoForward ? entries[++index] : null;
}
