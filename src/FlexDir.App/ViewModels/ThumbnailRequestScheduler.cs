using FlexDir.App.Threading;

using FlexDir.Core.Presentation;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 보이는 항목의 형식 아이콘과 썸네일을 요청하는 정책.
/// <para>
/// 얻어오는 것은 <see cref="IThumbnailSource"/> 의 일이고 그리는 것은 View 의 일이다.
/// 여기 있는 것은 <b>무엇을 언제 묻고 무엇을 묻지 않는가</b> 뿐이다 — 형식 아이콘을 먼저,
/// 확장자마다 한 번만, 클라우드 자리표시자는 건드리지 않고, 스크롤 밖은 취소하고, 실패는
/// 재시도하지 않는다.
/// </para>
/// <para>
/// <see cref="SetVisibleRange"/>·<see cref="Reset"/>·<see cref="DisposeAsync"/> 는 UI
/// 스레드에서만 부른다 (스크롤·폴더 전환·창 종료가 부른다). 요청은 그 밖에서 돌고
/// 속성 대입만 <see cref="IUiDispatcher"/> 로 되돌아온다 (CLAUDE.md §3).
/// </para>
/// </summary>
public sealed class ThumbnailRequestScheduler : IAsyncDisposable
{
    /// <summary>
    /// 동시에 열어 두는 shell 요청의 수. shell 호출은 동기 블로킹이고 네트워크·클라우드
    /// 항목에서 초 단위로 멈춘다 (docs/SHELL_NOTES.md §아이콘 함정 1) — 제한이 없으면
    /// 워커가 전부 그것으로 막힌다.
    /// </summary>
    public const int MaxConcurrentRequests = 4;

    private readonly IThumbnailSource source;
    private readonly IUiDispatcher dispatcher;
    private readonly SemaphoreSlim slots = new(MaxConcurrentRequests, MaxConcurrentRequests);

    /// <summary>
    /// (확장자, 디렉터리 여부, 크기) → 형식 아이콘 요청. 진행 중인 요청도 그대로 담기므로
    /// 같은 확장자 파일이 1000개여도 요청은 1회다 (docs/SHELL_NOTES.md §아이콘). 실패해
    /// <c>null</c> 로 끝난 요청도 남아 재시도를 막는다 (docs/PRD.md §4).
    /// <para>
    /// 크기가 키에 들어가는 이유: 뷰 모드마다 16·16·32·96 을 쓴다 (docs/DESIGN.md §2).
    /// 16px 아이콘을 96px 자리에 늘려 쓰면 뿌옇게 그려진다.
    /// </para>
    /// <para>
    /// 잠금이 없는 이유: 세대가 <see cref="pending"/> 으로 직렬화되고 이 사전은 세대 본문
    /// (<see cref="FillIconsAsync"/>)에서만 만져진다.
    /// </para>
    /// </summary>
    private readonly Dictionary<(string Extension, bool IsDirectory, int Size), Task<ThumbnailBitmap?>> typeIcons = [];

    /// <summary>
    /// 스케줄러의 수명. 형식 아이콘 요청이 쓰는 토큰이며 <see cref="DisposeAsync"/> 만
    /// 취소한다 — 확장자 아이콘은 폴더·스크롤과 무관하다.
    /// </summary>
    private readonly CancellationTokenSource lifetime = new();

    /// <summary>지금 보이는 항목. 참조로 비교한다 — 갱신이 만든 새 행 인스턴스는 다른 항목이다.</summary>
    private List<FileItemViewModel> visible = [];

    /// <summary>
    /// 지금 세대의 취소원. 스크롤·폴더 전환이 이것을 끊는다.
    /// <para>
    /// Dispose 하지 않는다: 링크도 타이머도 없어 해제할 자원이 없고, <c>Cancel()</c> 이
    /// 콜백을 그 자리에서 실행하므로 이 취소로 끝나는 세대가 자기 취소원을
    /// <c>Cancel</c> 호출 스택 안에서 Dispose 하게 된다.
    /// </para>
    /// </summary>
    private CancellationTokenSource? generation;

    private Task pending = Task.CompletedTask;
    private bool disposed;

    public ThumbnailRequestScheduler(IThumbnailSource source, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);

        this.source = source;
        this.dispatcher = dispatcher;
    }

    /// <summary>
    /// 예약된 작업이 모두 끝나면 완료된다. <see cref="SetVisibleRange"/> 가 <c>void</c> 인
    /// 이유(스크롤 이벤트는 기다려 주지 않는다) 때문에 관측 지점이 하나 필요하다 —
    /// <see cref="DisposeAsync"/> 도 이것을 기다린다.
    /// </summary>
    public Task WhenIdle => pending;

    /// <summary>
    /// 화면에 보이는 항목이 바뀔 때마다 부른다.
    /// 범위에 없어진 항목의 진행 중 요청은 취소되고, 썸네일은 해제된다.
    /// </summary>
    public void SetVisibleRange(IReadOnlyList<FileItemViewModel> visible, int requestedSize)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);

        // 창이 닫히는 중에도 스크롤 이벤트는 온다.
        if (disposed)
        {
            return;
        }

        var rows = new List<FileItemViewModel>(visible);
        var staying = new HashSet<FileItemViewModel>(rows);
        var leaving = this.visible.Where(row => !staying.Contains(row)).ToList();

        this.visible = rows;

        // 진행 중 요청을 먼저 끊는다 (docs/PRD.md §2 — 스크롤 중 취소). 아직 보이는 항목의
        // 요청까지 함께 끊긴다 — 취소는 시도로 세지 않으므로 이번 세대가 다시 묻는다.
        pending = RunAsync(pending, rows, leaving, requestedSize, NextGeneration());
    }

    /// <summary>폴더를 옮길 때 부른다. 진행 중 요청을 모두 취소한다.</summary>
    public void Reset()
    {
        if (disposed)
        {
            return;
        }

        generation?.Cancel();
        generation = null;

        // 이전 폴더의 행은 버려진다. 확장자 아이콘 사전은 비우지 않는다 — 확장자 아이콘은
        // 폴더와 무관하고, 다시 조회하면 폴더를 옮길 때마다 같은 값을 사 오는 셈이다.
        visible = [];
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        generation?.Cancel();
        lifetime.Cancel();

        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 종료 중의 취소는 정상이다.
        }

        lifetime.Dispose();
        slots.Dispose();
    }

    private CancellationTokenSource NextGeneration()
    {
        generation?.Cancel();
        generation = new CancellationTokenSource();

        return generation;
    }

    /// <summary>
    /// 한 세대. 이전 세대가 물러난 뒤에 시작한다 — 순서를 정하지 않으면 방금 취소한 요청의
    /// 결과가 이번 세대가 해제한 썸네일을 되살린다.
    /// </summary>
    private async Task RunAsync(
        Task previous,
        List<FileItemViewModel> rows,
        List<FileItemViewModel> leaving,
        int requestedSize,
        CancellationTokenSource owner)
    {
        try
        {
            await previous.ConfigureAwait(false);

            var token = owner.Token;

            await ReleaseAsync(leaving).ConfigureAwait(false);

            // 형식 아이콘을 먼저 채운다. 자리를 비워두지 않는다 (docs/UI_GUIDE.md §상태 표현).
            await FillIconsAsync(rows, requestedSize, token).ConfigureAwait(false);

            // 물어볼 수 없는 항목을 닫은 뒤 나머지의 썸네일을 요청한다.
            await CloseAsync(rows).ConfigureAwait(false);
            await LoadThumbnailsAsync(rows, requestedSize, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 취소는 정상 종료다. 다음 세대가 이 Task 를 기다리므로 오류로 남기지 않는다.
        }
    }

    /// <summary>
    /// 보이는 범위를 벗어난 항목의 썸네일을 해제한다. BGRA 버퍼가 항목당 36KB 라 10만 항목
    /// 폴더를 훑으면 유지할 수 없다.
    /// <para>
    /// <see cref="FileItemViewModel.Icon"/> 과 <see cref="FileItemViewModel.ThumbnailAttempted"/>
    /// 는 남긴다 — 다시 보일 때 빈칸이 되지 않게, 그리고 재요청이 나가지 않게 하는 것이
    /// 그 둘의 일이다. 취소된 세대에서도 해제는 한다.
    /// </para>
    /// </summary>
    private Task ReleaseAsync(List<FileItemViewModel> leaving)
    {
        if (leaving.Count == 0)
        {
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(() =>
        {
            foreach (var row in leaving)
            {
                row.Thumbnail = null;
            }
        });
    }

    private async Task FillIconsAsync(
        List<FileItemViewModel> rows,
        int requestedSize,
        CancellationToken token)
    {
        // 확장자로 묶는다. 파일마다 조회하면 대용량 폴더에서 그것만으로 멈춘다
        // (docs/SHELL_NOTES.md §아이콘).
        var groups = new Dictionary<(string, bool, int), List<FileItemViewModel>>();

        foreach (var row in rows)
        {
            var key = (row.Item.Extension, row.IsDirectory, requestedSize);

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(row);
        }

        foreach (var (key, group) in groups)
        {
            token.ThrowIfCancellationRequested();

            var icon = await TypeIconAsync(key).ConfigureAwait(false);

            // 조회가 실패했다. 사전에 남아 다시 묻지 않으며, 그 확장자는 아이콘 없이 간다.
            if (icon is null)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();

            await dispatcher.InvokeAsync(() =>
            {
                foreach (var row in group)
                {
                    row.Icon = icon;
                }
            }).ConfigureAwait(false);
        }
    }

    private Task<ThumbnailBitmap?> TypeIconAsync((string Extension, bool IsDirectory, int Size) key)
    {
        if (typeIcons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // 결과가 아니라 요청을 담는다 — 진행 중인 것을 담지 않으면 같은 확장자를 겹쳐 물을 때
        // 두 번 조회하게 된다.
        var request = RequestTypeIconAsync(key);
        typeIcons[key] = request;

        return request;
    }

    private async Task<ThumbnailBitmap?> RequestTypeIconAsync(
        (string Extension, bool IsDirectory, int Size) key)
    {
        // 세대 토큰이 아니라 수명 토큰이다. 확장자 아이콘은 폴더와 무관하고 사전은 폴더 전환에도
        // 남으므로(Reset 이 비우지 않는다) 스크롤·폴더 전환으로 버릴 이유가 없다.
        var token = lifetime.Token;

        await slots.WaitAsync(token).ConfigureAwait(false);

        try
        {
            return await source
                .GetTypeIconAsync(key.Extension, key.IsDirectory, key.Size, token)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 아이콘 하나 때문에 세대가 죽으면 그 폴더의 썸네일이 전부 사라진다.
            return null;
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>
    /// 썸네일을 물어볼 수 없는 항목을 닫는다. 디렉터리는 형식 아이콘이 전부고, 클라우드
    /// 자리표시자는 내용을 건드리면 다운로드가 트리거된다 (docs/SHELL_NOTES.md §열거 함정 3) —
    /// 목록을 스크롤한 것만으로 수 GB 를 내려받게 된다.
    /// </summary>
    private Task CloseAsync(List<FileItemViewModel> rows)
    {
        var closed = rows.Where(row => !row.ThumbnailAttempted && !CanRequestThumbnail(row)).ToList();

        if (closed.Count == 0)
        {
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(() =>
        {
            foreach (var row in closed)
            {
                row.ThumbnailAttempted = true;
            }
        });
    }

    private async Task LoadThumbnailsAsync(
        List<FileItemViewModel> rows,
        int requestedSize,
        CancellationToken token)
    {
        var requests = new List<Task>();

        foreach (var row in rows)
        {
            // 결론이 난 항목은 다시 묻지 않는다 — 성공이든 실패든 (docs/PRD.md §4).
            if (row.ThumbnailAttempted || !CanRequestThumbnail(row))
            {
                continue;
            }

            requests.Add(LoadThumbnailAsync(row, requestedSize, token));
        }

        // 전부 예약해도 shell 에 동시에 들어가는 것은 슬롯 수만큼이다.
        await Task.WhenAll(requests).ConfigureAwait(false);
    }

    private async Task LoadThumbnailAsync(
        FileItemViewModel row,
        int requestedSize,
        CancellationToken token)
    {
        ThumbnailBitmap? bitmap;

        try
        {
            await slots.WaitAsync(token).ConfigureAwait(false);

            try
            {
                bitmap = await source
                    .GetThumbnailAsync(row.Item.Location, requestedSize, token)
                    .ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 스크롤로 범위를 벗어났다. 시도로 세지 않는다 — 다시 보이면 그때 묻는다.
            return;
        }
        catch (Exception)
        {
            // 실패는 재시도하지 않는다 (docs/PRD.md §4). 형식 아이콘으로 대체된 채 남는다.
            await MarkAttemptedAsync(row).ConfigureAwait(false);

            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            // null 이어도 시도는 시도다. 없는 썸네일을 스크롤마다 다시 묻지 않는다.
            row.Thumbnail = bitmap;
            row.ThumbnailAttempted = true;
        }).ConfigureAwait(false);
    }

    private Task MarkAttemptedAsync(FileItemViewModel row)
        => dispatcher.InvokeAsync(() => row.ThumbnailAttempted = true);

    private static bool CanRequestThumbnail(FileItemViewModel row)
        => !row.IsDirectory && !row.Item.IsContentAccessRisky;
}
