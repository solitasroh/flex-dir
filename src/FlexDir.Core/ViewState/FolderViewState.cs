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
public sealed record FolderViewState(
    ViewMode Mode,
    IReadOnlyList<SortOrder> Sort,
    SortKey? GroupBy = null,
    IReadOnlyList<string>? Collapsed = null)
{
    // 위치 매개변수를 직접 받아 검사한다. 생성자 경로는 이 초기화식,
    // 'with' 경로는 아래 init 접근자가 맡는다.
    private readonly IReadOnlyList<SortOrder> sort = Validate(Sort);
    private readonly IReadOnlyList<string> collapsed = Normalize(Collapsed);

    /// <summary>Details + 이름 오름차순 + 그룹화 없음. 기억된 상태가 없는 폴더에 쓴다.</summary>
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

    /// <summary>
    /// 그룹 헤더로 나눌 기준 (docs/PRD-v2.md §6-1). <c>null</c> 이면 그룹화가 꺼진 것이고,
    /// 그때 목록 경로는 v1 과 같다. 정렬 기준과 같은 열거형을 쓴다 — 그룹 키가 곧 정렬
    /// 1차 키이므로 (<see cref="Grouping.FileItemGroups.WithGroupKey"/>) 둘이 갈리면
    /// 그룹 경계와 정렬 순서가 어긋난다.
    /// </summary>
    public SortKey? GroupBy { get; init; } = GroupBy;

    /// <summary>
    /// 접혀 있는 그룹의 라벨. 정렬 키와 달리 <b>비어 있는 것이 정상</b>이라 검사하지 않고,
    /// 순서·중복을 지운 집합으로 다룬다 — 왕복이 순서를 뒤집었다고 "바뀌었다" 가 되면
    /// 폴더를 떠날 때마다 쓸데없이 다시 쓴다.
    /// </summary>
    public IReadOnlyList<string> Collapsed
    {
        get => collapsed;
        init => collapsed = Normalize(value);
    }

    // 리스트 내용을 비교한다. 저장·복원 왕복은 다른 인스턴스를 내므로 참조 비교로는
    // "정렬이 바뀌었는가" 를 판정할 수 없다 — 계약 테스트가 파일 기반 구현체에서
    // 실패하게 된다. SortOrder 는 record 라 요소 비교는 값 비교다.
    public bool Equals(FolderViewState? other)
        => other is not null
            && Mode == other.Mode
            && GroupBy == other.GroupBy
            && Sort.SequenceEqual(other.Sort)
            && Collapsed.SequenceEqual(other.Collapsed);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(GroupBy);

        foreach (var order in Sort)
        {
            hash.Add(order);
        }

        foreach (var label in Collapsed)
        {
            hash.Add(label);
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

    /// <summary>
    /// 순서·중복을 지운다. 정렬해 두면 <see cref="Equals"/> 가 집합 비교가 되고, 저장 파일도
    /// 매번 같은 모양으로 나가 diff 가 흔들리지 않는다. 호출자가 넘긴 리스트를 나중에
    /// 바꿔도 상태가 흔들리지 않는 것은 <see cref="Validate"/> 와 같은 이유다.
    /// </summary>
    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? value)
        => value is null ? [] : [.. value.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>
/// Details 컬럼의 폭 (docs/DESIGN.md §2). <b>넷 다 픽셀이다</b> — 탐색기와 같이 경계를 끌면
/// 그 컬럼만 변하고 나머지는 폭을 지킨 채 밀린다. 남는 자리는 맨 뒤의 빈 열이 먹는다
/// (사용자 지적 2026-08-10: 이름을 <c>*</c> 로 두었더니 한 열을 늘릴 때 다른 열이 줄어드는
/// 시소가 됐다).
/// <para>
/// <b>페인마다 따로다</b> (사용자 지적 2026-08-10). 두 페인이 한 값을 나눠 쓰면 한쪽에서
/// 끌 때 반대편이 함께 움직인다 — 페인은 독립이라는 전제(docs/PRD.md §4)가 여기서도 같다.
/// </para>
/// <para>
/// 검증하지 않는다. 손상된 값은 쓰는 쪽(<c>PaneViewModel</c>)이 자른다.
/// </para>
/// </summary>
public sealed record PaneColumns(double Name, double Size, double Type, double Modified)
{
    public static PaneColumns Default { get; } = new(320, 90, 120, 140);
}

/// <summary>
/// 탭 하나가 기억하는 것 (docs/PRD-v2.md §17). <b>뷰 모드·정렬·그룹화는 없다</b> —
/// 그것은 폴더별이고 <see cref="FolderViewState"/> 가 정본이라, 같은 폴더를 연 두 탭은
/// 같은 뷰다 (ADR-018).
/// </summary>
/// <param name="Title">
/// 사용자가 바꾼 제목. <c>null</c> 이면 폴더 이름을 쓴다 — 그 판정은 ViewModel 이 한다.
/// </param>
public sealed record TabState(Locations.LocationId Folder, bool IsPinned = false, string? Title = null);

/// <summary>
/// 페인 하나의 탭 목록과 활성 탭 (docs/PRD-v2.md §17 · docs/ARCHITECTURE.md §4).
/// 페인마다 하나씩 갖는다 — 창 단위가 아니라 페인 단위다 (ADR-018).
/// </summary>
public sealed record PaneTabsState(IReadOnlyList<TabState> Tabs, int ActiveIndex = 0)
{
    private readonly IReadOnlyList<TabState> tabs = [.. Tabs ?? throw new ArgumentNullException(nameof(Tabs))];
    private readonly int activeIndex = Clamp(ActiveIndex, Tabs?.Count ?? 0);

    /// <summary>탭 하나짜리 목록. 구버전 저장 파일의 단수 필드가 이 모양으로 들어온다.</summary>
    public static PaneTabsState Single(Locations.LocationId folder) => new([new TabState(folder)]);

    /// <summary>호출자가 넘긴 리스트를 나중에 바꿔도 상태가 흔들리지 않게 복사한다.</summary>
    public IReadOnlyList<TabState> Tabs
    {
        get => tabs;
        init
        {
            tabs = [.. value ?? throw new ArgumentNullException(nameof(Tabs))];

            // 목록을 갈아 끼우면 번호가 그 목록 밖일 수 있다. 아래 init 과 순서가 갈리지
            // 않도록 여기서 다시 자른다 — 'with' 는 어느 쪽이 먼저 돌지 보장하지 않는다.
            activeIndex = Clamp(activeIndex, tabs.Count);
        }
    }

    /// <summary>
    /// 활성 탭의 번호. <b>범위를 벗어난 값은 던지지 않고 가장 가까운 자리로 잘린다</b> —
    /// 손상된 저장 파일 하나가 전역 상태 전체를 기본값으로 접으면 창 배치까지 잃는다
    /// (<see cref="GlobalViewState.TreeWidth"/> 와 같은 판단).
    /// </summary>
    public int ActiveIndex
    {
        get => activeIndex;
        init => activeIndex = Clamp(value, tabs.Count);
    }

    // 리스트 내용을 비교한다. 저장·복원 왕복은 다른 인스턴스를 내므로 참조 비교로는
    // "탭이 바뀌었는가" 를 판정할 수 없다 (FolderViewState 와 같은 이유).
    public bool Equals(PaneTabsState? other)
        => other is not null && ActiveIndex == other.ActiveIndex && Tabs.SequenceEqual(other.Tabs);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(activeIndex);

        foreach (var tab in tabs)
        {
            hash.Add(tab);
        }

        return hash.ToHashCode();
    }

    private static int Clamp(int index, int count)
        => count == 0 ? 0 : index < 0 ? 0 : index >= count ? count - 1 : index;
}

/// <summary>
/// 페인 하나가 기억하는 것 — 탭 목록과 Details 컬럼 폭 (docs/PRD-v2.md §18).
/// <para>
/// <b>컬럼 폭이 탭이 아니라 여기 붙는 이유</b>는 <see cref="PaneColumns"/> 주석에 있다 —
/// 소유자가 페인이라야 탭 전환마다 컬럼이 튀지 않는다. 분할이 들어오며 좌·우 두 벌이던
/// 것이 페인마다 한 벌이 됐고, 그러면서 탭 목록과 한 묶음이 됐다: <b>페인이 생기고
/// 사라지는 단위</b>가 곧 이 record 다.
/// </para>
/// </summary>
public sealed record PaneState(PaneTabsState Tabs, PaneColumns Columns)
{
    /// <summary>기억된 컬럼 폭이 없는 페인. 새로 만든 페인이 이 모양이다.</summary>
    public PaneState(PaneTabsState tabs)
        : this(tabs, PaneColumns.Default)
    {
    }
}

/// <summary>
/// 폴더와 무관한 전역 상태. 창 배치와 분할 모양, 트리의 모양 (docs/ARCHITECTURE.md §4).
/// </summary>
/// <param name="Panes">
/// 페인마다의 탭 목록과 컬럼 폭 (docs/PRD-v2.md §17·§18). <c>null</c> 은 <b>기억이 없다</b>는
/// 뜻이고 그때 시작 폴더 규칙이 자리를 채운다 — 페인 0개짜리 목록과는 다른 사건이다.
/// <para>
/// <b><see cref="PaneCount"/> 보다 길 수 있다.</b> 분할을 줄이는 것은 페인을 <b>접는</b>
/// 것이지 닫는 것이 아니라서(사용자 결정 2026-08-12), 접힌 페인도 여기 남아야 다시 폈을 때
/// 그대로 돌아온다 — 재시작을 건너서도 그렇다.
/// </para>
/// </param>
/// <param name="PaneCount">
/// <b>화면에 보이는</b> 페인 수 (1~<see cref="MaxPanes"/>). 배치는 프리셋이라 이 수 하나로
/// 정해진다 (사용자 결정 2026-08-12) — 어느 슬롯이 어디로 가는지는 View 가 든다
/// (<c>SplitLayout</c>).
/// </param>
/// <param name="RowRatio">
/// 가로 스플리터의 자리 — 위 칸이 차지하는 비율. <b>격자 전체에 하나다</b>: 4분할에서 좌·우가
/// 이 값을 나눠 써야 가로줄이 일직선으로 이어진다. 3분할에서는 오른쪽 열에만 쓰인다.
/// </param>
/// <param name="TreeVisible">
/// 폴더 트리를 보이는가 (docs/PRD-v2.md §10). 처음 켠 사람에게는 보인다 — 토글은 트리를
/// 보고 나서야 찾는다.
/// </param>
/// <param name="TreeWidth">
/// 트리 폭. <see cref="SplitterRatio"/> 와 달리 <b>검증하지 않는다</b> — 여기서 던지면
/// 손상된 값 하나가 전역 상태 전체를 기본값으로 접어 창 배치와 마지막 폴더까지 잃는다.
/// 폭은 쓰는 쪽(<c>FolderTreeViewModel.Width</c>)이 자르면 되는 값이다.
/// </param>
public sealed record GlobalViewState(
    double SplitterRatio,
    WindowPlacement? Window,
    IReadOnlyList<PaneState>? Panes = null,
    int PaneCount = 1,
    double RowRatio = 0.5,
    bool TreeVisible = true,
    double TreeWidth = GlobalViewState.DefaultTreeWidth)
{
    /// <summary>기억된 것이 없을 때의 트리 폭. 목록의 긴 폴더 이름이 대체로 들어간다.</summary>
    public const double DefaultTreeWidth = 220;

    /// <summary>
    /// 분할 프리셋의 끝 (사용자 결정 2026-08-12: 4분할까지). <b>이 수를 늘리는 것만으로는
    /// 늘어나지 않는다</b> — 슬롯이 어느 칸에 앉는지는 View 의 <c>SplitLayout</c> 이 자기
    /// 표로 들고 있고, 그 표도 함께 늘어야 한다.
    /// </summary>
    public const int MaxPanes = 4;

    private readonly double splitterRatio = Validate(SplitterRatio, nameof(SplitterRatio));
    private readonly double rowRatio = Validate(RowRatio, nameof(RowRatio));
    private readonly int paneCount = ClampCount(PaneCount);
    private readonly IReadOnlyList<PaneState>? panes = Copy(Panes);

    /// <summary>
    /// 절반 분할, 창 배치·마지막 폴더 기억 없음 — 시작 폴더는 폴백으로 간다.
    /// <b>1분할이다</b> (사용자 결정 2026-08-12): 기본화면은 한 칸이고, 이미 쓰던 사람은
    /// 저장 파일이 2분할을 들고 온다 (<c>JsonViewStateStore</c> 의 마이그레이션).
    /// </summary>
    public static GlobalViewState Default { get; } = new(0.5, null);

    /// <summary>
    /// 왼쪽 열이 차지하는 비율. 0 과 1 사이여야 한다 — 저장 파일이 손상돼 -3 이
    /// 들어오면 페인이 사라지는데, 그 시점에는 원인을 찾기 어렵다.
    /// </summary>
    public double SplitterRatio
    {
        get => splitterRatio;
        init => splitterRatio = Validate(value, nameof(SplitterRatio));
    }

    /// <inheritdoc cref="RowRatio"/>
    public double RowRatio
    {
        get => rowRatio;
        init => rowRatio = Validate(value, nameof(RowRatio));
    }

    /// <summary>
    /// 화면에 보이는 페인 수. <b>범위를 벗어난 값은 던지지 않고 가장 가까운 자리로 잘린다</b> —
    /// 5 에는 짐작할 수 있는 뜻이 있고(4), 그 하나 때문에 전역 상태 전체가 기본값으로 접히면
    /// 창 배치와 마지막 폴더까지 잃는다 (<see cref="PaneTabsState.ActiveIndex"/> 와 같은 판단).
    /// <para>
    /// <b>비율과 갈리는 지점이 여기다.</b> 비율은 손상됐을 때 짐작할 수 있는 뜻이 없다 —
    /// -3 이 0.3 이었는지 알 길이 없으므로 파일 전체를 기본값으로 접는 편이 낫다.
    /// </para>
    /// </summary>
    public int PaneCount
    {
        get => paneCount;
        init => paneCount = ClampCount(value);
    }

    /// <summary>
    /// 호출자가 넘긴 리스트를 나중에 바꿔도 상태가 흔들리지 않게 복사한다
    /// (<see cref="PaneTabsState.Tabs"/> 와 같은 이유).
    /// </summary>
    public IReadOnlyList<PaneState>? Panes
    {
        get => panes;
        init => panes = Copy(value);
    }

    /// <summary>
    /// 리스트 내용을 비교한다. 저장·복원 왕복은 다른 인스턴스를 내므로 참조 비교로는
    /// "페인이 바뀌었는가" 를 판정할 수 없다 — 계약 테스트가 왕복을 이 비교로 본다
    /// (<see cref="PaneTabsState"/>·<see cref="FolderViewState"/> 와 같은 이유).
    /// </summary>
    public bool Equals(GlobalViewState? other)
        => other is not null
            && SplitterRatio.Equals(other.SplitterRatio)
            && RowRatio.Equals(other.RowRatio)
            && PaneCount == other.PaneCount
            && Window == other.Window
            && TreeVisible == other.TreeVisible
            && TreeWidth.Equals(other.TreeWidth)
            && (Panes is null ? other.Panes is null : other.Panes is not null && Panes.SequenceEqual(other.Panes));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(splitterRatio);
        hash.Add(rowRatio);
        hash.Add(paneCount);
        hash.Add(Window);
        hash.Add(TreeVisible);
        hash.Add(TreeWidth);

        foreach (var pane in panes ?? [])
        {
            hash.Add(pane);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// 기억이 없는 것(<see langword="null"/>)과 페인 0개는 다른 사건이므로 <c>null</c> 을
    /// 빈 리스트로 접지 않는다.
    /// </summary>
    private static IReadOnlyList<PaneState>? Copy(IReadOnlyList<PaneState>? value)
        => value is null ? null : [.. value];

    private static int ClampCount(int value)
        => value < 1 ? 1 : value > MaxPanes ? MaxPanes : value;

    private static double Validate(double value, string name)
    {
        // 부정형으로 쓴다. NaN 은 모든 관계 비교가 false 라서
        // (value <= 0 || value >= 1) 형태로는 빠져나간다.
        if (value is not (> 0 and < 1))
        {
            throw new ArgumentOutOfRangeException(name, value, "스플리터 비율은 0 과 1 사이여야 한다.");
        }

        return value;
    }
}

/// <summary>창의 마지막 위치와 크기. 논리 픽셀 단위다.</summary>
public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized);
