using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.App.Navigation;
using FlexDir.App.Threading;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Presentation;
using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;

namespace FlexDir.App.ViewModels;

/// <summary>페인이 지금 무엇을 하고 있는지 (docs/UI_GUIDE.md §상태 표현).</summary>
public enum PaneStatus
{
    Idle,
    Enumerating,
    Empty,
    Error,
}

/// <summary>
/// 페인 하나. 폴더를 열고 목록을 점진적으로 채운다.
/// <para>
/// 열거는 <see cref="EnumerationSession"/> 으로만 한다 — <c>IFolderSource</c> 를 직접 부르면
/// 폴더 이탈 시의 결과 격리(세대 번호)가 사라진다. UI 스레드로 옮기는 지점은
/// <see cref="IUiDispatcher"/> 하나뿐이다 (CLAUDE.md §3).
/// </para>
/// </summary>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly EnumerationSession session;
    private readonly ITypeNameProvider typeNames;
    private readonly IViewStateStore viewStates;
    private readonly IUiDispatcher dispatcher;
    private readonly IFormatProvider culture;
    private readonly TimeZoneInfo timeZone;
    private readonly PaneHistory history = new();

    /// <summary>확장자 → 유형 이름. 확장자마다 한 번만 조회한다 (docs/SHELL_NOTES.md §아이콘).</summary>
    private readonly Dictionary<string, string> fileTypeNames = new(StringComparer.Ordinal);

    /// <summary>디렉터리의 유형 이름. 확장자가 없으므로 사전과 따로 둔다.</summary>
    private string? directoryTypeName;

    private LocationId? currentLocation;
    private PaneStatus status = PaneStatus.Idle;
    private string statusText = string.Empty;

    private ViewMode viewMode = FolderViewState.Default.Mode;
    private IReadOnlyList<SortOrder> sort = FolderViewState.Default.Sort;

    /// <summary><see cref="sort"/> 에서 만든다. 정렬이 바뀔 때만 새로 만든다.</summary>
    private FileItemComparer comparer = FileItemComparer.Default;

    public PaneViewModel(
        IFolderSource folderSource,
        ITypeNameProvider typeNames,
        IViewStateStore viewStates,
        IUiDispatcher dispatcher,
        IFormatProvider culture,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(folderSource);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(viewStates);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(timeZone);

        session = new EnumerationSession(folderSource);
        this.typeNames = typeNames;
        this.viewStates = viewStates;
        this.dispatcher = dispatcher;
        this.culture = culture;
        this.timeZone = timeZone;

        // 선택이 바뀌면 상태표시줄이 따라간다 (docs/DESIGN.md §6).
        Selection.PropertyChanged += OnSelectionChanged;
    }

    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();

    /// <summary>
    /// 선택. 이름으로 보관하므로 정렬·뷰 전환·갱신을 지나도 같은 항목이 선택돼 있다
    /// (CLAUDE.md §4).
    /// </summary>
    public PaneSelection Selection { get; } = new();

    public LocationId? CurrentLocation
    {
        get => currentLocation;
        private set
        {
            if (SetProperty(ref currentLocation, value))
            {
                OnPropertyChanged(nameof(AddressText));
            }
        }
    }

    /// <summary>주소창에 보이는 경로. 확장 접두사(<c>\\?\</c>)는 사용자에게 보이지 않는다.</summary>
    public string AddressText => CurrentLocation?.DisplayPath ?? string.Empty;

    public PaneStatus Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>상태표시줄 문구. 개수·진행·오류 사유가 모두 여기로 나간다.</summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>목록을 그리는 방식. 폴더마다 기억한다 (docs/PRD.md §2).</summary>
    public ViewMode ViewMode
    {
        get => viewMode;
        private set => SetProperty(ref viewMode, value);
    }

    /// <summary>
    /// 정렬 기준. v1 은 항상 키 하나다 — <see cref="FileItemComparer"/> 는 다중 키를 받지만
    /// 그것을 만드는 조작이 UI 에 없다. 없는 조작을 위한 상태를 쌓지 않는다.
    /// </summary>
    public IReadOnlyList<SortOrder> Sort
    {
        get => sort;
        private set
        {
            // 리스트는 참조 비교라 SetProperty 로 걸러지지 않는다. 저장·복원 왕복은 매번
            // 다른 인스턴스를 내므로 내용으로 판정해야 같은 폴더를 다시 읽을 때 조용하다.
            if (sort.SequenceEqual(value))
            {
                return;
            }

            sort = [.. value];
            comparer = new FileItemComparer(sort);
            OnPropertyChanged();
        }
    }

    public bool CanGoBack => history.CanGoBack;

    public bool CanGoForward => history.CanGoForward;

    public bool CanGoUp => CurrentLocation is { } current && current.TryGetParent(out _);

    /// <summary>지정한 위치를 연다. 히스토리에 기록한다.</summary>
    public Task NavigateAsync(LocationId location, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        history.Navigate(location);

        return OpenAsync(location, ct);
    }

    /// <summary>
    /// 문자열 주소를 파싱해 연다. 파싱 실패는 <see cref="PaneStatus.Error"/> 로 간다 —
    /// 주소창에 오타를 내는 것은 정상 조작이고, 예외로 만들면 호출부마다 잡아야 한다.
    /// </summary>
    public Task NavigateAsync(string address, CancellationToken ct = default)
    {
        if (LocationId.TryParse(address, out var location, out var error))
        {
            return NavigateAsync(location, ct);
        }

        return ShowParseErrorAsync(address, error);
    }

    public Task GoBackAsync(CancellationToken ct = default)
    {
        var target = history.GoBack();

        return target is null ? Task.CompletedTask : OpenAsync(target, ct);
    }

    public Task GoForwardAsync(CancellationToken ct = default)
    {
        var target = history.GoForward();

        return target is null ? Task.CompletedTask : OpenAsync(target, ct);
    }

    /// <summary>상위 폴더를 연다. 히스토리에 기록한다 — 뒤로가 자식으로 돌아간다.</summary>
    public Task GoUpAsync(CancellationToken ct = default)
    {
        return CurrentLocation is { } current && current.TryGetParent(out var parent)
            ? NavigateAsync(parent, ct)
            : Task.CompletedTask;
    }

    /// <summary>현재 폴더를 다시 읽는다. 히스토리에 기록하지 않는다.</summary>
    public Task RefreshAsync(CancellationToken ct = default)
    {
        return CurrentLocation is { } current ? OpenAsync(current, ct) : Task.CompletedTask;
    }

    /// <summary>
    /// 컬럼 헤더 클릭. 같은 키를 다시 누르면 방향만 반전하고, 다른 키는 오름차순으로
    /// 시작한다 (탐색기와 같은 동작).
    /// <para>
    /// 열거를 다시 시작하지 않는다 — 이미 받은 항목을 다시 배치할 뿐이다. 10만 항목
    /// 폴더에서 헤더 한 번에 몇 초를 다시 기다리게 할 수 없다 (docs/UI_GUIDE.md §원칙 3).
    /// 열거 중이면 이후 도착하는 배치가 새 정렬로 붙는다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task ChangeSortAsync(SortKey key)
    {
        var current = sort[0];

        Sort = [current.Key == key ? new SortOrder(key, !current.Descending) : new SortOrder(key)];
        SortItems();

        return SaveViewStateAsync();
    }

    /// <summary>
    /// 뷰 모드를 바꾼다. 목록을 다시 읽지 않는다 — 같은 데이터의 다른 표현일 뿐이고
    /// 뷰 전환은 <c>DataTemplate</c> 교체다 (ADR-002).
    /// </summary>
    [RelayCommand]
    private Task ChangeViewModeAsync(ViewMode mode)
    {
        ViewMode = mode;

        return SaveViewStateAsync();
    }

    /// <summary>낡아진 열거의 결과. 상태를 건드리지 않고 물러난다.</summary>
    private static LoadResult Stale => new(true, null);

    private async Task OpenAsync(LocationId location, CancellationToken ct)
    {
        var opened = await LoadAsync(location, ct).ConfigureAwait(false);

        if (opened.IsStale)
        {
            return;
        }

        // 경로가 사라진 경우만 자동으로 움직인다 (docs/PRD.md §4). 권한 문제는 경로가
        // 맞으므로 그 자리에 남는다.
        if (opened.Error is not { Kind: LocationErrorKind.NotFound } missing)
        {
            return;
        }

        // 상위도 없으면 (드라이브 루트) 오류 상태로 멈춘다.
        if (!location.TryGetParent(out var parent))
        {
            return;
        }

        var reason = LocationErrorMessages.Describe(missing.Kind, missing.Location);

        // 한 단계만 올라간다. 연쇄로 올라가면 사용자가 어디로 갔는지 모른다.
        history.Navigate(parent);
        var fallback = await LoadAsync(parent, ct).ConfigureAwait(false);

        if (fallback.IsStale || fallback.Error is not null)
        {
            // 상위도 열 수 없으면 그 사유가 남아야 한다.
            return;
        }

        // 왜 여기 있는지 알려야 한다 — 항목 개수보다 이 사유가 먼저다.
        await dispatcher.InvokeAsync(() => StatusText = reason).ConfigureAwait(false);
    }

    private async Task<LoadResult> LoadAsync(LocationId location, CancellationToken ct)
    {
        // 이전 열거를 접고 새 세대를 연다. 뷰 상태 조회보다 먼저다 — 폴더 전환의 순서를
        // 정하는 것이 이 호출이고, 앞에 await 를 두면 느린 저장소가 그 순서를 뒤집는다.
        var run = session.Start(location);

        // 열거를 시작하기 전에 읽는다. 첫 배치부터 그 폴더의 정렬로 붙어야 한다.
        var view = await LoadViewStateAsync(location, ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() =>
        {
            // 저장소가 느리면 그 사이에 다른 폴더로 옮겨갔을 수 있다. 낡은 폴더의 뷰 상태와
            // 경로를 지금 반영하면 화면이 현재 폴더와 어긋난다.
            if (run.IsStale)
            {
                return;
            }

            var movedAway = !location.Equals(currentLocation);

            // 목록은 건드리지 않는다. 먼저 비우면 폴더 전환마다 빈 화면이 번쩍인다
            // (docs/UI_GUIDE.md §상태 표현).
            SetLocation(location);
            ViewMode = view.Mode;
            Sort = view.Sort;
            Status = PaneStatus.Enumerating;
            StatusText = StatusSummary.ForEnumerating(0, culture);

            if (movedAway)
            {
                // 다른 폴더의 이름이 남으면 새 폴더의 엉뚱한 항목이 선택된다. 같은 폴더를
                // 다시 읽는 것(새로 고침·감시 갱신)은 선택을 유지한다 (CLAUDE.md §4).
                Selection.Clear();
            }
        }).ConfigureAwait(false);

        var shown = 0;
        var batches = 0;

        try
        {
            await foreach (var batch in run.BatchesAsync(ct).ConfigureAwait(false))
            {
                var rows = await BuildRowsAsync(batch, ct).ConfigureAwait(false);

                if (run.IsStale)
                {
                    return Stale;
                }

                shown += rows.Count;
                batches++;

                var replaceExisting = batches == 1;
                var soFar = shown;

                await dispatcher.InvokeAsync(() =>
                {
                    // 검사와 반영 사이에 새 Start 가 끼어들 수 있다. 실제 Dispatcher 는 이
                    // 동작들을 UI 스레드에서 직렬화하므로 여기서 한 번 더 보면 낡은 배치가
                    // 목록에 섞이지 않는다.
                    if (run.IsStale)
                    {
                        return;
                    }

                    // 첫 배치가 도착할 때 이전 폴더의 목록을 교체한다.
                    if (replaceExisting)
                    {
                        Items.ReplaceAll(rows);
                    }
                    else
                    {
                        Items.AddRange(rows);
                    }

                    StatusText = StatusSummary.ForEnumerating(soFar, culture);
                }).ConfigureAwait(false);
            }
        }
        catch (LocationAccessException error)
        {
            if (run.IsStale)
            {
                // 이미 떠난 폴더의 실패다. 새 폴더의 오류로 표시하면 안 된다.
                return Stale;
            }

            await dispatcher.InvokeAsync(() =>
            {
                // 경로는 되돌리지 않는다 (docs/PRD.md §4). 목록은 비운다 — 이전 폴더의
                // 항목이 남으면 잘못된 폴더의 내용으로 보인다.
                Items.ReplaceAll([]);
                Status = PaneStatus.Error;
                StatusText = LocationErrorMessages.Describe(error.Kind, error.Location);
            }).ConfigureAwait(false);

            return new LoadResult(false, error);
        }

        if (run.IsStale)
        {
            return Stale;
        }

        await dispatcher.InvokeAsync(() =>
        {
            if (run.IsStale)
            {
                return;
            }

            // 배치는 각자 정렬돼 붙었을 뿐이다. 완전한 정렬은 열거 종료 시 한 번 보장한다.
            if (batches > 1)
            {
                SortItems();
            }

            if (shown == 0)
            {
                Items.ReplaceAll([]);
            }

            if (Selection.Count > 0)
            {
                // 같은 폴더를 다시 읽었다. 사라진 이름만 떨군다 — 남기면 상태표시줄의 선택
                // 개수가 실제와 어긋난다. 선택이 비어 있으면 목록을 훑을 이유가 없다.
                Selection.Retain([.. Items.Select(row => row.Name)]);
            }

            Status = shown == 0 ? PaneStatus.Empty : PaneStatus.Idle;
            RefreshStatusText();
        }).ConfigureAwait(false);

        return new LoadResult(false, null);
    }

    private Task ShowParseErrorAsync(string? address, LocationParseError error)
    {
        // 경로도 목록도 건드리지 않는다 — 열지 않았으므로 보이는 것은 여전히 맞다.
        return dispatcher.InvokeAsync(() =>
        {
            Status = PaneStatus.Error;
            StatusText = DescribeParseError(error, address);
        });
    }

    private async Task<List<FileItemViewModel>> BuildRowsAsync(
        IReadOnlyList<FileItem> batch,
        CancellationToken ct)
    {
        // 배치 안에서만 정렬한다. 배치마다 전체를 다시 정렬하면 항목이 쌓일수록 느려진다.
        var items = new List<FileItem>(batch);
        items.Sort(comparer);

        var rows = new List<FileItemViewModel>(items.Count);

        foreach (var item in items)
        {
            rows.Add(new FileItemViewModel(
                item,
                SizeFormatter.ForItem(item, culture),
                TimestampFormatter.Format(item.ModifiedUtc, timeZone, culture),
                await TypeNameAsync(item, ct).ConfigureAwait(false)));
        }

        return rows;
    }

    private async ValueTask<string> TypeNameAsync(FileItem item, CancellationToken ct)
    {
        if (item.IsDirectory)
        {
            return directoryTypeName ??= await typeNames
                .GetTypeNameAsync(string.Empty, isDirectory: true, ct)
                .ConfigureAwait(false);
        }

        if (fileTypeNames.TryGetValue(item.Extension, out var cached))
        {
            return cached;
        }

        var typeName = await typeNames
            .GetTypeNameAsync(item.Extension, isDirectory: false, ct)
            .ConfigureAwait(false);

        fileTypeNames[item.Extension] = typeName;

        return typeName;
    }

    private void SortItems()
    {
        // Reset 알림이 나가지만 선택은 PaneSelection 이 이름으로 들고 있어 재배치로 잃지 않는다.
        // View 의 선택을 Selection 에서 다시 맞추는 것은 수동 UI phase 의 일이다.
        var sorted = new List<FileItemViewModel>(Items);
        sorted.Sort((left, right) => comparer.Compare(left.Item, right.Item));

        Items.ReplaceAll(sorted);
    }

    /// <summary>
    /// 폴더에 기억된 뷰 상태. 없으면 <see cref="FolderViewState.Default"/> 다 —
    /// 이전 폴더의 설정을 물려주지 않는다. 폴더별 기억이 존재 이유다 (docs/PRD.md §2).
    /// </summary>
    private async Task<FolderViewState> LoadViewStateAsync(LocationId folder, CancellationToken ct)
    {
        try
        {
            return await viewStates.TryLoadAsync(folder, ct).ConfigureAwait(false)
                ?? FolderViewState.Default;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 뷰 상태는 캐시다 (CLAUDE.md §4). 폴더를 못 여는 것과는 전혀 다른 사건이므로
            // 상태표시줄에 올리지 않는다 — 목록이 멀쩡한데 오류로 보이면 사용자는 파일이
            // 사라진 줄 안다.
            return FolderViewState.Default;
        }
    }

    /// <summary>현재 폴더의 뷰 상태를 저장한다. 아직 아무 폴더도 열지 않았으면 대상이 없다.</summary>
    private async Task SaveViewStateAsync()
    {
        if (CurrentLocation is not { } folder)
        {
            return;
        }

        try
        {
            await viewStates
                .SaveAsync(folder, new FolderViewState(ViewMode, Sort), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 읽기와 같은 이유로 삼킨다. 다음에 열 때 기본값으로 보일 뿐이다.
        }
    }

    /// <summary>
    /// 상태표시줄 문구를 개수·선택으로 다시 만든다.
    /// <para>
    /// 열거 중에는 진행 표시가 선택 요약보다 우선이다 — 총 개수가 아직 확정되지 않았다.
    /// 오류 상태에서도 덮지 않는다: 사유가 개수보다 먼저다 (docs/UI_GUIDE.md §상태 표현).
    /// </para>
    /// </summary>
    private void RefreshStatusText()
    {
        if (Status is PaneStatus.Enumerating or PaneStatus.Error)
        {
            return;
        }

        // ForSelection 은 선택이 0 개면 ForItems 와 같은 문자열을 낸다 — 여기서 분기하지 않는다.
        StatusText = Items.Count == 0
            ? StatusSummary.Empty
            : StatusSummary.ForSelection(Items.Count, Selection.Count, SelectedBytes(), culture);
    }

    /// <summary>
    /// 선택한 항목의 크기 합. 디렉터리는 0 으로 센다 — 폴더 용량 계산은 v1 범위 밖이며
    /// <c>SizeFormatter.ForItem</c> 과 같은 규칙이어야 한다 (docs/PRD.md §3).
    /// </summary>
    private long SelectedBytes()
    {
        var total = 0L;

        foreach (var row in Items)
        {
            if (!row.IsDirectory && Selection.IsSelected(row.Name))
            {
                total += row.Item.Size;
            }
        }

        return total;
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        // 개수가 같아도 무엇이 선택됐는지 바뀌면 크기 합이 달라진다. 내용 변경 알림을 본다.
        if (args.PropertyName == nameof(PaneSelection.SelectedNames))
        {
            RefreshStatusText();
        }
    }

    private void SetLocation(LocationId location)
    {
        CurrentLocation = location;

        // 히스토리에서 파생되는 값이라 값 비교로 걸러낼 수 없다. 이동마다 알린다.
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    /// <summary>
    /// 주소창 입력이 거부된 사유. <see cref="LocationErrorMessages"/> 는 열거 실패
    /// (<see cref="LocationErrorKind"/>)만 다루므로 파싱 실패 문구는 여기 둔다 —
    /// 주소를 문자열로 받는 것은 ViewModel 뿐이다.
    /// </summary>
    private static string DescribeParseError(LocationParseError error, string? address)
    {
        var wording = error switch
        {
            LocationParseError.Empty => "경로를 입력하세요",
            LocationParseError.RelativePath => "전체 경로를 입력하세요",
            LocationParseError.AlternateDataStream => "경로에 ':' 를 쓸 수 없습니다",
            LocationParseError.InvalidCharacter => "경로에 쓸 수 없는 문자가 있습니다",
            LocationParseError.NetworkPathNotSupported => "네트워크 경로는 아직 지원하지 않습니다",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "알 수 없는 분류다."),
        };

        return string.IsNullOrWhiteSpace(address) ? wording : $"{wording} — {address.Trim()}";
    }

    /// <summary>한 번의 <see cref="LoadAsync"/> 결과. 오류가 없으면 <see cref="Error"/> 는 null 이다.</summary>
    private readonly record struct LoadResult(bool IsStale, LocationAccessException? Error);
}
