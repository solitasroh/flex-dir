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
using FlexDir.Core.Operations;
using FlexDir.Core.Presentation;
using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;
using FlexDir.Core.Watching;

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
/// <para>
/// 폴더를 여는 동안 감시도 함께 돈다. 외부 변경은 목록에 반영되지만 <b>선택은 유지된다</b> —
/// 갱신 때마다 선택이 풀리면 감시가 없느니만 못하다 (ADR-011 · CLAUDE.md §4).
/// </para>
/// </summary>
public sealed partial class PaneViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>새 폴더의 기본 이름. 겹치면 구현체가 유일한 이름을 만든다.</summary>
    private const string NewFolderName = "새 폴더";

    /// <summary>
    /// 한 번에 적용하는 변경 알림의 최대 개수. 파일 100개를 복사하면 알림이 100개 이상 온다.
    /// <para>
    /// 시간이 아니라 <b>개수</b>로만 끊는다 — 디바운스를 넣으면 묶음 경계가 실행 속도에 따라
    /// 달라지고, 그 원인을 찾는 비용이 자율 실행에서 가장 크다.
    /// </para>
    /// </summary>
    private const int WatchBatchSize = 64;

    private readonly IFolderSource folderSource;
    private readonly IFolderWatcher folderWatcher;
    private readonly EnumerationSession session;
    private readonly ITypeNameProvider typeNames;
    private readonly IViewStateStore viewStates;
    private readonly IFileOperations fileOperations;
    private readonly IClipboardBridge clipboard;
    private readonly IItemActivator activator;
    private readonly IUiDispatcher dispatcher;
    private readonly IFormatProvider culture;
    private readonly TimeZoneInfo timeZone;
    private readonly PaneHistory history = new();

    /// <summary>
    /// 확장자 → 유형 이름. 확장자마다 한 번만 조회한다 (docs/SHELL_NOTES.md §아이콘).
    /// <para>
    /// <see cref="typeNameGate"/> 로 감싼다. 이 캐시는 UI 스레드 밖에서 <b>두 경로가 동시에</b>
    /// 만진다 — 열거(<see cref="BuildRowsAsync"/>)와 감시 갱신(<see cref="ApplyAsync"/>)이고,
    /// <see cref="RowAsync"/> 는 성능 때문에 의도적으로 dispatcher 밖이라 UI 스레드가 둘을
    /// 직렬화해 주지 않는다. 잠금 없는 <c>Dictionary</c> 는 동시 쓰기에 내부 구조가 깨져
    /// 조회가 매달릴 수 있다.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, string> fileTypeNames = new(StringComparer.Ordinal);

    /// <summary>디렉터리의 유형 이름. 확장자가 없으므로 사전과 따로 둔다.</summary>
    private string? directoryTypeName;

    /// <summary>
    /// 유형 이름 캐시의 잠금. 조회 자체는 이 잠금 <b>밖에서</b> 한다 — shell 조회는 동기
    /// 블로킹이고 네트워크·클라우드 항목에서 초 단위로 멈춘다 (docs/SHELL_NOTES.md §아이콘 함정 1).
    /// <para>
    /// 그래서 같은 확장자를 겹쳐 물으면 조회가 두 번 나갈 수 있다. 막지 않는다: 지켜야 할 규칙은
    /// "파일마다 묻지 마라"(10만 항목에 확장자 열 종류면 열 번)이고, 진행 중인 조회를 공유하려면
    /// 결과 대신 <c>Task</c> 를 캐시해야 하는데 그러면 폴더를 옮겨 <b>취소된</b> 조회가 사전에
    /// 남아 다음 폴더까지 오염시킨다.
    /// </para>
    /// </summary>
    private readonly Lock typeNameGate = new();

    /// <summary>진행 중인 감시. 폴더를 옮길 때마다 교체된다.</summary>
    private WatchRun? watch;

    private LocationId? currentLocation;
    private PaneStatus status = PaneStatus.Idle;
    private string statusText = string.Empty;
    private string? renamingName;

    private ViewMode viewMode = FolderViewState.Default.Mode;
    private IReadOnlyList<SortOrder> sort = FolderViewState.Default.Sort;

    /// <summary><see cref="sort"/> 에서 만든다. 정렬이 바뀔 때만 새로 만든다.</summary>
    private FileItemComparer comparer = FileItemComparer.Default;

    public PaneViewModel(
        IFolderSource folderSource,
        IFolderWatcher folderWatcher,
        ITypeNameProvider typeNames,
        IViewStateStore viewStates,
        IFileOperations fileOperations,
        IClipboardBridge clipboard,
        IItemActivator activator,
        IUiDispatcher dispatcher,
        IFormatProvider culture,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(folderSource);
        ArgumentNullException.ThrowIfNull(folderWatcher);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(viewStates);
        ArgumentNullException.ThrowIfNull(fileOperations);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(timeZone);

        // 감시 알림을 받은 항목은 다시 읽어야 한다 (TryGetItemAsync) — 세션은 폴더 열거만 안다.
        this.folderSource = folderSource;
        this.folderWatcher = folderWatcher;
        session = new EnumerationSession(folderSource);
        this.typeNames = typeNames;
        this.viewStates = viewStates;
        this.fileOperations = fileOperations;
        this.clipboard = clipboard;
        this.activator = activator;
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

    /// <summary>
    /// 이름 편집 중인 항목의 이름. 편집 중이 아니면 null.
    /// <para>
    /// 인라인 편집기를 띄우는 것은 View 의 일이다 — 여기 있는 것은 "무엇을 편집하는가"
    /// 뿐이고, 이름으로 들고 있는 이유는 선택과 같다 (갱신이 행 인스턴스를 교체한다).
    /// 새로 만든 폴더처럼 아직 목록에 없는 이름일 수도 있다.
    /// </para>
    /// </summary>
    public string? RenamingName
    {
        get => renamingName;
        private set => SetProperty(ref renamingName, value);
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

    /// <summary>
    /// 선택한 항목을 다른 폴더로 복사한다. 페인 간 복사가 이 경로를 쓴다.
    /// <para>
    /// 대상 폴더가 현재 폴더여도 막지 않는다 — shell 이 사본을 만드는 정상 조작이다.
    /// </para>
    /// </summary>
    public Task CopySelectionToAsync(LocationId destinationFolder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destinationFolder);

        var items = SelectedLocations();

        return items.Count == 0
            ? Task.CompletedTask
            : RunAsync(token => fileOperations.CopyAsync(items, destinationFolder, token), ct);
    }

    /// <summary>선택한 항목을 다른 폴더로 이동한다. 페인 간 이동이 이 경로를 쓴다.</summary>
    public Task MoveSelectionToAsync(LocationId destinationFolder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destinationFolder);

        // 제자리 이동은 아무 일도 아니다. 그대로 shell 에 넘기면 오류 대화상자가 뜬다.
        if (destinationFolder.Equals(currentLocation))
        {
            return Task.CompletedTask;
        }

        var items = SelectedLocations();

        return items.Count == 0
            ? Task.CompletedTask
            : RunAsync(token => fileOperations.MoveAsync(items, destinationFolder, token), ct);
    }

    /// <summary>선택이 있는지. 조작 커맨드의 <c>CanExecute</c> 다.</summary>
    private bool HasSelection => Selection.Count > 0;

    /// <summary>선택이 정확히 하나인지. 무엇의 이름을 바꾸는지 정해져야 한다.</summary>
    private bool HasSingleSelection => Selection.Count == 1;

    /// <summary>낡아진 열거의 결과. 상태를 건드리지 않고 물러난다.</summary>
    private static LoadResult Stale => new(true, null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySelection()
    {
        var items = SelectedLocations();

        // CanExecute 로 막지만 그것과 실행 사이에 감시 갱신이 끼어들 수 있다.
        if (items.Count > 0)
        {
            clipboard.SetCopy(items);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CutSelection()
    {
        var items = SelectedLocations();

        if (items.Count > 0)
        {
            clipboard.SetCut(items);
        }
    }

    /// <summary>
    /// 클립보드의 항목을 <b>현재 폴더</b>로 붙여넣는다. 잘라내기였으면 이동이다.
    /// 목록은 손대지 않는다 — 감시가 갱신한다 (CLAUDE.md §4).
    /// </summary>
    [RelayCommand]
    private Task PasteAsync(CancellationToken ct)
    {
        // 아직 아무 폴더도 열지 않았으면 붙여넣을 자리가 없다.
        if (currentLocation is not { } folder)
        {
            return Task.CompletedTask;
        }

        // 다른 앱이 클립보드를 채우는 것은 관측할 수 없다 — 물어보는 것이 유일한 방법이다.
        if (!clipboard.TryGetPaste(out var items, out var isMove) || items.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunAsync(
            token => isMove
                ? fileOperations.MoveAsync(items, folder, token)
                : fileOperations.CopyAsync(items, folder, token),
            ct);
    }

    /// <summary>선택한 항목을 휴지통으로 보낸다 (영구 삭제 경로는 v1 에 없다).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteSelectionAsync(CancellationToken ct)
    {
        var items = SelectedLocations();

        return items.Count == 0
            ? Task.CompletedTask
            : RunAsync(token => fileOperations.DeleteAsync(items, token), ct);
    }

    /// <summary>
    /// 현재 폴더에 새 폴더를 만들고 <b>그 이름의 편집을 시작한다</b> (탐색기와 같은 흐름).
    /// <para>
    /// 편집 대상은 요청한 이름이 아니라 만들어진 위치의 이름이다 — 이름이 겹치면 구현체가
    /// 유일한 이름을 만들고, 요청한 이름으로 편집을 열면 없는 항목의 이름을 바꾸게 된다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task CreateFolderAsync(CancellationToken ct)
    {
        if (currentLocation is not { } folder)
        {
            return;
        }

        try
        {
            var created = await fileOperations
                .CreateFolderAsync(folder, NewFolderName, ct)
                .ConfigureAwait(false);

            // 목록에 넣지 않는다 — 감시가 갱신한다 (CLAUDE.md §4).
            await dispatcher.InvokeAsync(() => RenamingName = created.Name).ConfigureAwait(false);
        }
        catch (LocationAccessException error)
        {
            await ShowReasonAsync(error).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 더블클릭·Enter. 폴더면 이 페인에서 열고, 파일이면 연결 프로그램을 실행한다.
    /// <para>
    /// 폴더 진입을 <see cref="IItemActivator"/> 에 맡기지 않는다 — shell 이 새 탐색기 창을
    /// 띄운다. 클라우드 자리표시자는 따로 분기하지 않는다: 다운로드를 트리거하는 것은
    /// 사용자가 의도한 행위이고, 접근을 피해야 하는 것은 썸네일뿐이다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task ActivateAsync(FileItemViewModel? item, CancellationToken ct)
    {
        // 빈 곳을 더블클릭하면 대상이 없다.
        if (item is null)
        {
            return Task.CompletedTask;
        }

        return item.IsDirectory
            ? NavigateAsync(item.Item.Location, ct)
            : RunAsync(token => activator.ActivateAsync(item.Item.Location, token), ct);
    }

    /// <summary>이름 편집을 시작한다. 선택이 정확히 하나일 때만.</summary>
    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void BeginRename()
    {
        if (Selection.Count != 1)
        {
            return;
        }

        RenamingName = Selection.SelectedNames.First();
    }

    [RelayCommand]
    private void CancelRename() => RenamingName = null;

    /// <summary>
    /// 편집을 확정한다. 이름이 비었거나 그대로면 편집만 취소한다.
    /// <para>
    /// 실패하면 사유만 상태표시줄에 올리고 <b>목록은 건드리지 않는다</b> — 성공했을 때도
    /// 마찬가지다. 진실원천은 파일시스템이고 갱신은 감시가 한다 (CLAUDE.md §4).
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task CommitRenameAsync(string? newName, CancellationToken ct)
    {
        // 편집 중이 아니면 확정할 것이 없다.
        if (renamingName is not { } original || currentLocation is not { } folder)
        {
            return Task.CompletedTask;
        }

        // 성공이든 실패든 편집은 여기서 닫힌다.
        RenamingName = null;

        if (string.IsNullOrWhiteSpace(newName)
            || StringComparer.OrdinalIgnoreCase.Equals(newName, original))
        {
            return Task.CompletedTask;
        }

        // 위치는 폴더와 이름으로 만든다 — 새로 만든 폴더는 아직 목록에 없다.
        return RunAsync(token => fileOperations.RenameAsync(folder.Combine(original), newName, token), ct);
    }

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

    /// <summary>
    /// 폴더를 열고 감시를 함께 시작한다.
    /// <para>
    /// 감시는 열거와 <b>같은 시점</b>에 시작한다. 열거가 끝난 뒤에 걸면 그 사이에 일어난
    /// 변경을 잃는다 — 대용량·네트워크 폴더에서는 그 창이 초 단위다. 대신 모아둔 변경은
    /// 열거가 끝난 뒤에 적용한다 (채우는 중에 섞으면 첫 배치 교체가 그것을 덮는다).
    /// </para>
    /// </summary>
    private async Task<LoadResult> LoadAsync(LocationId location, CancellationToken ct)
    {
        // 이전 열거를 접고 새 세대를 연다. 뷰 상태 조회보다 먼저다 — 폴더 전환의 순서를
        // 정하는 것이 이 호출이고, 앞에 await 를 두면 느린 저장소가 그 순서를 뒤집는다.
        var run = session.Start(location);
        var watching = StartWatching(run);
        var listed = false;

        try
        {
            var result = await FillAsync(run, location, ct).ConfigureAwait(false);

            // 열거가 실패했으면 이 폴더의 목록은 만들어지지 않았다. 그 사실을 감시에 알려야
            // 알림만으로 목록을 채우는 것을 막을 수 있다.
            listed = result.Error is null;

            return result;
        }
        finally
        {
            // 열거가 어떻게 끝났든(성공·실패·낡음) 모아둔 변경을 풀어준다. 이걸 놓치면 감시
            // 루프가 영원히 기다리고 DisposeAsync 가 매달린다.
            watching.MarkEnumerated(listed);
        }
    }

    private async Task<LoadResult> FillAsync(EnumerationRun run, LocationId location, CancellationToken ct)
    {
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
        // 예외 <b>종류</b>로 가르지 않는다. 계약은 LocationAccessException 이지만
        // (IFolderSource) 구현체는 FindFirstFileExW P/Invoke 위에 서고, 계약을 어긴 예외가
        // 여기서 새면 Status 가 Enumerating 에 남는다 — 그 상태에서는 RefreshStatusText 도
        // 물러나므로(사유조차 나오지 않는다) 페인이 '읽는 중' 에서 영구 정지한다.
        // 취소만 통과시키고 나머지는 전부 "열지 못했다" 로 다룬다.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (run.IsStale)
            {
                // 이미 떠난 폴더의 실패다. 새 폴더의 오류로 표시하면 안 된다.
                return Stale;
            }

            // 사유 문구가 없는 예외에는 분류할 수 없는 실패의 문구를 쓴다. 예외 메시지를 그대로
            // 올리지 않는다 — 문구를 만드는 곳은 LocationErrorMessages 한 곳이어야 한다.
            var failure = error as LocationAccessException
                ?? new LocationAccessException(LocationErrorKind.Unknown, location);

            await dispatcher.InvokeAsync(() =>
            {
                // 경로는 되돌리지 않는다 (docs/PRD.md §4). 목록은 비운다 — 이전 폴더의
                // 항목이 남으면 잘못된 폴더의 내용으로 보이고, 읽다 만 폴더의 일부가 남으면
                // 완전한 목록처럼 보인다.
                Items.ReplaceAll([]);
                Status = PaneStatus.Error;
                StatusText = LocationErrorMessages.Describe(failure.Kind, failure.Location);
            }).ConfigureAwait(false);

            return new LoadResult(false, failure);
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
            rows.Add(await RowAsync(item, ct).ConfigureAwait(false));
        }

        return rows;
    }

    /// <summary>
    /// 항목 하나의 줄. 표시 문자열은 여기서 굳으므로 항목이 바뀌면 줄을 다시 만들어야 한다.
    /// </summary>
    private async ValueTask<FileItemViewModel> RowAsync(FileItem item, CancellationToken ct)
        => new(
            item,
            SizeFormatter.ForItem(item, culture),
            TimestampFormatter.Format(item.ModifiedUtc, timeZone, culture),
            await TypeNameAsync(item, ct).ConfigureAwait(false));

    private async ValueTask<string> TypeNameAsync(FileItem item, CancellationToken ct)
    {
        if (Cached(item) is { } known)
        {
            return known;
        }

        var typeName = await LookupTypeNameAsync(item, ct).ConfigureAwait(false);

        lock (typeNameGate)
        {
            if (item.IsDirectory)
            {
                directoryTypeName = typeName;
            }
            else
            {
                // 실패한 조회도 남는다 — 재시도하지 않는다 (docs/PRD.md §4). 재시도하면 유형
                // 이름을 못 읽는 폴더에서 파일마다 shell 호출이 나간다.
                fileTypeNames[item.Extension] = typeName;
            }
        }

        return typeName;
    }

    /// <summary>이미 조회한 유형 이름. 없으면 null 이다 — 빈 문자열은 조회된 결과다.</summary>
    private string? Cached(FileItem item)
    {
        lock (typeNameGate)
        {
            return item.IsDirectory
                ? directoryTypeName
                : fileTypeNames.GetValueOrDefault(item.Extension);
        }
    }

    /// <summary>
    /// 유형 이름을 묻는다. <b>실패는 빈 문자열이다</b> (<see cref="ITypeNameProvider"/>).
    /// <para>
    /// 계약을 어기고 예외를 내는 구현체가 있어도 폴더가 통째로 열리지 않아야 한다. 열거 경로에서
    /// 이 예외가 새면 <see cref="Status"/> 가 <see cref="PaneStatus.Enumerating"/> 에 남고,
    /// 그 상태에서는 <see cref="RefreshStatusText"/> 도 물러나므로 사유조차 나오지 않는다 —
    /// 페인이 '읽는 중' 에서 영구 정지한다. 감시 경로에서는 갱신이 조용히 멈춘다.
    /// </para>
    /// <para>
    /// 취소만 그대로 내보낸다. 이미 떠난 폴더의 줄을 계속 만들 이유가 없다.
    /// </para>
    /// </summary>
    private async ValueTask<string> LookupTypeNameAsync(FileItem item, CancellationToken ct)
    {
        try
        {
            return await typeNames
                .GetTypeNameAsync(
                    item.IsDirectory ? string.Empty : item.Extension,
                    item.IsDirectory,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 이 폴더의 감시를 시작하고 이전 감시를 접는다.
    /// <para>
    /// 이전 감시의 완료를 <b>기다리지 않는다</b> — 오버플로 폴백이 감시 루프 안에서 이 경로를
    /// 다시 타므로 기다리면 자기 자신을 기다리는 교착이 된다. 낡은 감시가 목록을 건드리지
    /// 못하게 막는 것은 대기가 아니라 세대(<see cref="EnumerationRun.IsStale"/>)다.
    /// </para>
    /// </summary>
    private WatchRun StartWatching(EnumerationRun run)
    {
        var next = new WatchRun(run);
        var previous = Interlocked.Exchange(ref watch, next);

        previous?.Cancel();

        // Task.Run 으로 떼어낸다. 감시를 거는 것 자체가 shell 호출이므로 (실제 구현체는
        // FileSystemWatcher 를 만든다) 호출자의 스레드에서 시작하면 UI 스레드에서 shell 을
        // 부르게 된다 (CLAUDE.md §3).
        next.Loop = Task.Run(() => WatchLoopAsync(next));

        return next;
    }

    /// <summary>
    /// 감시 스트림을 읽어 묶음 단위로 적용한다.
    /// <para>
    /// 묶음 경계는 두 가지다 — 모인 것이 <see cref="WatchBatchSize"/> 개이거나, 스트림이 잠시
    /// 비어 더 읽을 것이 없을 때. 시간으로 끊지 않는다.
    /// </para>
    /// </summary>
    private async Task WatchLoopAsync(WatchRun watching)
    {
        var pending = new List<FolderChange>();

        try
        {
            await using var changes = folderWatcher
                .WatchAsync(watching.Folder, watching.Token)
                .GetAsyncEnumerator(watching.Token);

            var move = changes.MoveNextAsync();

            while (true)
            {
                // 아직 완료되지 않은 MoveNextAsync = 지금 읽을 것이 없다. 여기서 적용한다.
                // 완료를 기다리기 전에 봐야 하므로 이 검사를 await 앞에 둔다.
                if (pending.Count > 0 && !move.IsCompleted)
                {
                    await ApplyAsync(watching, pending).ConfigureAwait(false);
                }

                if (!await move.ConfigureAwait(false))
                {
                    break;
                }

                pending.Add(changes.Current);

                if (pending.Count >= WatchBatchSize)
                {
                    await ApplyAsync(watching, pending).ConfigureAwait(false);
                }

                move = changes.MoveNextAsync();
            }

            // 스트림이 끝났다 (감시 대상이 사라졌다). 받아둔 것은 적용한다.
            if (pending.Count > 0)
            {
                await ApplyAsync(watching, pending).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 폴더를 옮겼거나 페인을 닫았다. 정상 종료다 (IFolderWatcher 계약).
        }
        catch (Exception)
        {
            // 감시가 죽었다. 목록은 여전히 유효하므로 건드리지 않고 Status 도 올리지 않는다 —
            // 오류로 표시하면 파일이 사라진 것처럼 보이고, 감시가 죽은 것은 사용자가 손쓸 수
            // 있는 일이 아니다. 새로 고침으로 회복한다.
        }
    }

    /// <summary>
    /// 모인 알림을 목록에 반영한다. <paramref name="pending"/> 은 비워진다.
    /// </summary>
    private async Task ApplyAsync(WatchRun watching, List<FolderChange> pending)
    {
        // 열거가 끝날 때까지 모아둔다. 취소로도 풀리므로 폴더를 옮기면 모아둔 것은 버려진다.
        await watching.Enumerated.WaitAsync(watching.Token).ConfigureAwait(false);

        if (!watching.Listed)
        {
            // 열거가 실패한 폴더다. 알림을 적용하면 상태표시줄에는 사유가 남아 있는데 항목은
            // 보이는 상태가 된다 — 읽지도 못한 폴더의 내용으로 보인다 (FillAsync 가 실패 시
            // 목록을 비우는 것과 같은 이유).
            //
            // 감시도 여기서 접는다. 이 폴더에서 올 수 있는 것은 오해를 부르는 갱신뿐이고,
            // 회복은 새로 고침이다 — 그때 감시도 새로 걸린다 (docs/PRD.md §4).
            pending.Clear();
            watching.Cancel();

            return;
        }

        var batch = ChangeBatch.From(pending);
        pending.Clear();

        if (batch.RequiresFullRefresh)
        {
            // 이벤트가 유실됐다. 무시하면 목록이 파일시스템과 어긋난 채 남는다
            // (docs/SHELL_NOTES.md §폴더 감시). 선택은 이름으로 남아 재열거 뒤 Retain 이
            // 복원한다. 감시의 토큰을 넘기지 않는다 — 이 호출이 그 토큰을 취소한다.
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // 알림은 "무엇이 바뀌었는지" 만 말한다. 값은 파일시스템에서 다시 읽는다 (CLAUDE.md §4).
        var upserts = new List<FileItem>(batch.NeedsRefresh.Count);
        var removals = new List<string>(batch.Removals);

        foreach (var name in batch.NeedsRefresh)
        {
            var item = await folderSource
                .TryGetItemAsync(watching.Folder.Combine(name), watching.Token)
                .ConfigureAwait(false);

            // 알림과 실제가 어긋나는 것은 정상이다 — 없으면 목록에서도 없다.
            if (item is null)
            {
                removals.Add(name);
            }
            else
            {
                upserts.Add(item);
            }
        }

        var current = new List<FileItem>();
        var selection = new List<string>();
        var rows = new Dictionary<string, FileItemViewModel>(StringComparer.OrdinalIgnoreCase);

        // 목록을 읽는 것도 UI 스레드에서 한다 — 열거가 동시에 목록을 갈아치울 수 있다.
        await dispatcher.InvokeAsync(() =>
        {
            if (watching.IsStale)
            {
                return;
            }

            foreach (var row in Items)
            {
                current.Add(row.Item);
                rows[row.Name] = row;
            }

            selection.AddRange(Selection.SelectedNames);
        }).ConfigureAwait(false);

        if (watching.IsStale)
        {
            return;
        }

        var result = ListReconciler.Apply(
            current, selection, upserts, removals, batch.Renames, comparer);

        // 바뀐 항목의 줄은 다시 만든다. 유형 이름 조회가 비동기라 UI 스레드 밖에서 한다.
        var merged = new List<FileItemViewModel>(result.Items.Count);

        foreach (var item in result.Items)
        {
            merged.Add(rows.TryGetValue(item.Name, out var kept) && kept.Item == item
                ? kept
                : await RowAsync(item, watching.Token).ConfigureAwait(false));
        }

        await dispatcher.InvokeAsync(() =>
        {
            // 검사와 반영 사이에 폴더가 바뀔 수 있다. 실제 Dispatcher 는 이 동작들을 UI
            // 스레드에서 직렬화하므로 여기서 한 번 더 보면 낡은 알림이 목록에 섞이지 않는다.
            if (watching.IsStale)
            {
                return;
            }

            MergeItems(merged);

            // reconcile 이 낸 선택을 그대로 반영한다 — 비우지 않는다 (ADR-011).
            Selection.ReplaceWith(result.Selection);

            // 빈 폴더가 됐거나 다시 채워졌을 수 있다. 열거 중·오류 상태는 건드리지 않는다.
            if (Status is PaneStatus.Idle or PaneStatus.Empty)
            {
                Status = Items.Count == 0 ? PaneStatus.Empty : PaneStatus.Idle;
            }

            RefreshStatusText();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 목록을 <paramref name="merged"/> 와 같게 만든다.
    /// <para>
    /// <c>ReplaceAll</c>·<c>AddRange</c> 를 쓰지 않는다 — 그 <c>Reset</c> 알림이 WPF
    /// <c>ListView</c> 의 선택을 날린다 (ADR-011). 개별 <c>Add</c>·<c>Remove</c>·<c>Move</c>·
    /// 교체 알림만 낸다.
    /// </para>
    /// </summary>
    private void MergeItems(List<FileItemViewModel> merged)
    {
        var keep = new HashSet<string>(merged.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var row in merged)
        {
            keep.Add(row.Name);
        }

        // 사라진 줄부터 뺀다. 뒤에서부터 — 앞에서 지우면 인덱스가 밀린다.
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(Items[index].Name))
            {
                Items.RemoveAt(index);
            }
        }

        // 남은 줄은 이름이 유일하므로, 앞에서부터 자리를 맞추면 뒤쪽만 훑어도 충분하다.
        for (var index = 0; index < merged.Count; index++)
        {
            var row = merged[index];
            var found = IndexOfRow(row.Name, index);

            if (found < 0)
            {
                // 새 항목이다. 정렬 위치가 곧 이 자리다 — 맨 끝에 붙이지 않는다.
                Items.Insert(index, row);
                continue;
            }

            // 정렬 키가 바뀌어 자리가 달라진 줄.
            if (found != index)
            {
                Items.Move(found, index);
            }

            // 내용이 바뀐 줄. 표시 문자열이 생성 시점에 굳으므로 인스턴스를 교체한다.
            if (!ReferenceEquals(Items[index], row))
            {
                Items[index] = row;
            }
        }
    }

    private int IndexOfRow(string name, int from)
    {
        for (var index = from; index < Items.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(Items[index].Name, name))
            {
                return index;
            }
        }

        return -1;
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

    /// <summary>
    /// 선택한 항목의 위치. <b>화면 순서</b>로 낸다 — <c>PaneSelection</c> 은 집합이라 순서를
    /// 보장하지 않고, 조작의 항목 순서가 실행마다 달라지면 재현이 안 된다.
    /// </summary>
    private List<LocationId> SelectedLocations()
    {
        var items = new List<LocationId>(Selection.Count);

        foreach (var row in Items)
        {
            if (Selection.IsSelected(row.Name))
            {
                items.Add(row.Item.Location);
            }
        }

        return items;
    }

    /// <summary>
    /// 조작을 돌리고 실패를 상태표시줄로 옮긴다. 예외를 밖으로 던지지 않는다 — 커맨드에서
    /// 새는 예외는 잡을 사람이 없다.
    /// <para>
    /// <see cref="Status"/> 는 건드리지 않는다. 조작이 실패한 것과 폴더를 열 수 없는 것은
    /// 다른 사건이고, 목록은 여전히 맞다.
    /// </para>
    /// </summary>
    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        try
        {
            await operation(ct).ConfigureAwait(false);
        }
        catch (LocationAccessException error)
        {
            await ShowReasonAsync(error).ConfigureAwait(false);
        }
    }

    private Task ShowReasonAsync(LocationAccessException error)
        => dispatcher.InvokeAsync(
            () => StatusText = LocationErrorMessages.Describe(error.Kind, error.Location));

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        // 개수가 같아도 무엇이 선택됐는지 바뀌면 크기 합이 달라진다. 내용 변경 알림을 본다.
        if (args.PropertyName != nameof(PaneSelection.SelectedNames))
        {
            return;
        }

        RefreshStatusText();

        // 선택이 조작 커맨드의 CanExecute 다. 알리지 않으면 툴바 버튼이 그대로 회색이다.
        CopySelectionCommand.NotifyCanExecuteChanged();
        CutSelectionCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
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

    /// <summary>감시와 열거를 모두 접고 완료를 기다린다.</summary>
    public async ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref watch, null);

        current?.Cancel();

        // 열거를 먼저 접는다. 감시 루프가 열거 완료를 기다리고 있을 수 있고, 그 대기는
        // 취소로도 풀리지만 순서를 이렇게 두면 남은 배치까지 조용히 끝난다.
        await session.DisposeAsync().ConfigureAwait(false);

        if (current is not null)
        {
            // 감시 루프는 예외를 밖으로 내지 않는다 — 여기서 기다리는 것은 종료뿐이다.
            await current.Loop.ConfigureAwait(false);
        }
    }

    /// <summary>한 번의 <see cref="LoadAsync"/> 결과. 오류가 없으면 <see cref="Error"/> 는 null 이다.</summary>
    private readonly record struct LoadResult(bool IsStale, LocationAccessException? Error);

    /// <summary>
    /// 한 폴더의 감시. <see cref="EnumerationRun"/> 과 <b>세대를 공유한다</b> — 낡은 감시의
    /// 알림을 버리는 판정이 그 세대이고, 그 격리가 없으면 이전 폴더의 변경이 새 폴더 목록에
    /// 섞인다 (전작 잔버그의 원천이다).
    /// </summary>
    private sealed class WatchRun(EnumerationRun run)
    {
        // Dispose 하지 않는다. run 의 완료와 폴더 전환의 취소가 경합하면 이미 Dispose 된
        // 원본에 Cancel 이 들어와 ObjectDisposedException 이 된다 (EnumerationRun 과 같은 이유).
        private readonly CancellationTokenSource cts = new();

        // RunContinuationsAsynchronously: 이 신호를 켜는 스레드는 목록을 채우는 스레드다.
        // 감시 루프의 이어붙은 코드를 그 스레드에서 그대로 돌리면 채우는 손이 멈춘다.
        private readonly TaskCompletionSource enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LocationId Folder => run.Folder;

        /// <summary>더 새로운 폴더 열기가 있었는가. 있으면 이 감시의 알림은 버린다.</summary>
        public bool IsStale => run.IsStale;

        public CancellationToken Token => cts.Token;

        /// <summary>열거가 끝났는가. 그때까지 모인 변경은 적용하지 않고 들고 있는다.</summary>
        public Task Enumerated => enumerated.Task;

        /// <summary>
        /// 이 폴더의 목록이 실제로 만들어졌는가. 열거가 실패했으면 false 다 —
        /// <see cref="Enumerated"/> 는 "끝났는가" 만 말하므로 성공과 실패를 가르지 못한다.
        /// </summary>
        public bool Listed { get; private set; }

        /// <summary>감시 루프. <see cref="DisposeAsync"/> 만 이것을 기다린다.</summary>
        public Task Loop { get; set; } = Task.CompletedTask;

        public void MarkEnumerated(bool listed)
        {
            // 신호보다 먼저 쓴다. 기다리던 쪽이 풀린 뒤에 쓰면 그쪽이 옛 값을 본다.
            Listed = listed;

            enumerated.TrySetResult();
        }

        public void Cancel() => cts.Cancel();
    }
}
