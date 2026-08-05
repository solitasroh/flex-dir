using FlexDir.Core.Sorting;

namespace FlexDir.Core.ViewState;

/// <summary>
/// 목록을 그리는 방식. 이름은 docs/DESIGN.md §2 의 네 뷰
/// (Details · 목록 · 타일 · 큰 아이콘) 와 1:1 로 대응한다.
/// </summary>
public enum ViewMode
{
    Details,
    List,
    Tiles,
    LargeIcons,
}

/// <summary>
/// 폴더 하나의 뷰 상태 (docs/PRD.md §2 폴더별 기억). 영구 저장되는 값이며
/// 소유자는 <see cref="IViewStateStore"/> 다 (docs/ARCHITECTURE.md §4).
/// </summary>
public sealed record FolderViewState(ViewMode Mode, IReadOnlyList<SortOrder> Sort)
{
    // 위치 매개변수를 직접 받아 검사한다. 생성자 경로는 이 초기화식,
    // 'with' 경로는 아래 init 접근자가 맡는다.
    private readonly IReadOnlyList<SortOrder> sort = Validate(Sort);

    /// <summary>Details + 이름 오름차순. 기억된 상태가 없는 폴더에 쓴다.</summary>
    public static FolderViewState Default { get; } = new(ViewMode.Details, [new SortOrder(SortKey.Name)]);

    /// <summary>
    /// 앞에서부터 적용할 정렬 기준. 비어 있으면 <see cref="ArgumentException"/> —
    /// 통과시키면 <see cref="FileItemComparer"/> 가 훨씬 나중에 터지고, 저장 파일이
    /// 손상된 경우 원인 지점과 증상 지점이 멀어진다.
    /// </summary>
    public IReadOnlyList<SortOrder> Sort
    {
        get => sort;
        init => sort = Validate(value);
    }

    // 리스트 내용을 비교한다. 저장·복원 왕복은 다른 인스턴스를 내므로 참조 비교로는
    // "정렬이 바뀌었는가" 를 판정할 수 없다 — 계약 테스트가 파일 기반 구현체에서
    // 실패하게 된다. SortOrder 는 record 라 요소 비교는 값 비교다.
    public bool Equals(FolderViewState? other)
        => other is not null && Mode == other.Mode && Sort.SequenceEqual(other.Sort);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);

        foreach (var order in Sort)
        {
            hash.Add(order);
        }

        return hash.ToHashCode();
    }

    /// <summary>호출자가 넘긴 리스트를 나중에 바꿔도 상태가 흔들리지 않게 복사한다.</summary>
    private static IReadOnlyList<SortOrder> Validate(IReadOnlyList<SortOrder> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Count == 0)
        {
            throw new ArgumentException("정렬 기준이 비어 있다.", nameof(Sort));
        }

        return [.. value];
    }
}

/// <summary>
/// 폴더와 무관한 전역 상태. 창 배치와 스플리터 비율 (docs/ARCHITECTURE.md §4).
/// </summary>
public sealed record GlobalViewState(
    double SplitterRatio,
    WindowPlacement? Window,
    Locations.LocationId? LeftFolder = null,
    Locations.LocationId? RightFolder = null)
{
    private readonly double splitterRatio = Validate(SplitterRatio);

    /// <summary>절반 분할, 창 배치·마지막 폴더 기억 없음 — 시작 폴더는 폴백으로 간다.</summary>
    public static GlobalViewState Default { get; } = new(0.5, null);

    /// <summary>
    /// 좌 페인이 차지하는 비율. 0 과 1 사이여야 한다 — 저장 파일이 손상돼 -3 이
    /// 들어오면 페인이 사라지는데, 그 시점에는 원인을 찾기 어렵다.
    /// </summary>
    public double SplitterRatio
    {
        get => splitterRatio;
        init => splitterRatio = Validate(value);
    }

    private static double Validate(double value)
    {
        // 부정형으로 쓴다. NaN 은 모든 관계 비교가 false 라서
        // (value <= 0 || value >= 1) 형태로는 빠져나간다.
        if (value is not (> 0 and < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(SplitterRatio), value, "스플리터 비율은 0 과 1 사이여야 한다.");
        }

        return value;
    }
}

/// <summary>창의 마지막 위치와 크기. 논리 픽셀 단위다.</summary>
public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized);
