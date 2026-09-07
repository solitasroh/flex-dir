using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IThumbnailSource"/> 의 기록용 fake. shell 을 부르지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 실제 구현체(<c>IShellItemImageFactory</c>·
/// <c>SHGetFileInfoW</c>)는 <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009).
/// </para>
/// <para>
/// <b>캐시하지 않는다.</b> 확장자마다 한 번만 조회하는 것은 호출자의 정책이고
/// (docs/SHELL_NOTES.md §아이콘), 여기서 캐시하면 그 정책을 검증할 수단이 사라진다 —
/// <see cref="TypeIconRequests"/> 는 들어온 요청을 전부 기록한다.
/// </para>
/// <para>
/// 호출자가 요청을 여러 개 겹쳐 내므로 기록은 잠금으로 보호한다. 단정은 조용해진 뒤에 읽는다.
/// </para>
/// </summary>
public sealed class FakeThumbnailSource : IThumbnailSource
{
    /// <summary>형식 아이콘 픽셀을 채우는 값. 아이콘과 썸네일을 구별하는 표식이다.</summary>
    public const byte IconMark = 0x11;

    /// <summary>썸네일 픽셀을 채우는 값.</summary>
    public const byte ThumbnailMark = 0x22;

    /// <summary>항목 아이콘 주입에 쓰라고 둔 표식 값. 형식 아이콘·썸네일과 갈린다.</summary>
    public const byte ItemIconMark = 0x33;

    private readonly object gate = new();
    private int concurrent;

    /// <summary>들어온 썸네일 요청. 취소·실패로 끝난 것도 남는다.</summary>
    public List<(LocationId Item, int RequestedSize)> ThumbnailRequests { get; } = [];

    /// <summary>들어온 형식 아이콘 요청.</summary>
    public List<(string Extension, bool IsDirectory, int RequestedSize)> TypeIconRequests { get; } = [];

    /// <summary>들어온 항목 아이콘 요청. 취소·실패로 끝난 것도 남는다.</summary>
    public List<(LocationId Item, int RequestedSize)> ItemIconRequests { get; } = [];

    /// <summary>
    /// 경로별로 낼 항목 아이콘. 넣지 않은 경로는 <c>null</c> 을 받는다 — "없거나 실패하면
    /// <c>null</c>" 이 포트 계약이라 fake 의 기본값이 곧 실패 경로다.
    /// </summary>
    public Dictionary<LocationId, ThumbnailBitmap> ItemIcons { get; } = [];

    /// <summary>
    /// 썸네일 요청이 이 Task 를 기다린 뒤에 값을 낸다. 기본값은 이미 완료된 Task 다 —
    /// 미완료 Task 를 넣으면 "요청이 진행 중" 인 상태를 만들 수 있다 (동시 요청 수와
    /// 스크롤 취소를 관측하는 수단이다). 취소는 이 대기 중에도 관측한다.
    /// </summary>
    public Task ThumbnailGate { get; set; } = Task.CompletedTask;

    /// <summary>이 이름의 항목은 썸네일이 <c>null</c> 이다 (예외가 아니다).</summary>
    public HashSet<string> MissingThumbnails { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>썸네일 요청이 던질 예외. null 이면 던지지 않는다.</summary>
    public Exception? ThumbnailFailure { get; set; }

    /// <summary>형식 아이콘 요청이 던질 예외. null 이면 던지지 않는다.</summary>
    public Exception? TypeIconFailure { get; set; }

    /// <summary>관측된 최대 동시 요청 수. 호출자의 동시 요청 제한을 재는 눈금이다.</summary>
    public int PeakConcurrency { get; private set; }

    /// <summary>취소가 관측된 횟수.</summary>
    public int CancellationsObserved { get; private set; }

    public async ValueTask<ThumbnailBitmap?> GetThumbnailAsync(
        LocationId item,
        int requestedSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (gate)
        {
            // 실패해도·취소돼도 기록은 먼저 남긴다 — "불렸는데 취소됐다" 와 "아예 안 불렸다"
            // 는 다른 사건이다.
            ThumbnailRequests.Add((item, requestedSize));
            concurrent++;
            PeakConcurrency = Math.Max(PeakConcurrency, concurrent);
        }

        try
        {
            await ArriveAsync(ct);

            if (ThumbnailFailure is { } failure)
            {
                throw failure;
            }

            return MissingThumbnails.Contains(item.Name) ? null : Bitmap(requestedSize, ThumbnailMark);
        }
        finally
        {
            lock (gate)
            {
                concurrent--;
            }
        }
    }

    public ValueTask<ThumbnailBitmap?> GetTypeIconAsync(
        string extension,
        bool isDirectory,
        int requestedSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);

        lock (gate)
        {
            TypeIconRequests.Add((extension, isDirectory, requestedSize));
        }

        ObserveCancellation(ct);

        if (TypeIconFailure is { } failure)
        {
            throw failure;
        }

        return ValueTask.FromResult<ThumbnailBitmap?>(Bitmap(requestedSize, IconMark));
    }

    public ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 구현체(ShellThumbnailSource)가 명시적으로 던지는 검증이다. 다른 둘은 Bitmap 생성이
        // 대신 걸러 주지만, 이 메서드는 미주입 경로에서 비트맵을 만들지 않으므로 직접 던진다.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);

        lock (gate)
        {
            ItemIconRequests.Add((item, requestedSize));
        }

        ObserveCancellation(ct);

        return ValueTask.FromResult(ItemIcons.GetValueOrDefault(item));
    }

    /// <summary>같은 항목으로 들어온 썸네일 요청 횟수.</summary>
    public int CountThumbnailRequests(LocationId item)
        => ThumbnailRequests.Count(request => request.Item.Equals(item));

    /// <summary>같은 항목으로 들어온 항목 아이콘 요청 횟수. 크기는 보지 않는다.</summary>
    public int CountItemIconRequests(LocationId item)
        => ItemIconRequests.Count(request => request.Item.Equals(item));

    /// <summary>같은 확장자로 들어온 형식 아이콘 요청 횟수. 크기는 보지 않는다.</summary>
    public int CountTypeIconRequests(string extension, bool isDirectory)
        => TypeIconRequests.Count(
            request => request.Extension == extension && request.IsDirectory == isDirectory);

    /// <summary>한 값으로 채운 BGRA32 정사각 비트맵. 합성물임이 드러나야 한다.</summary>
    public static ThumbnailBitmap Bitmap(int size, byte mark)
    {
        var pixels = new byte[size * size * 4];
        Array.Fill(pixels, mark);

        return new ThumbnailBitmap(size, size, pixels);
    }

    /// <summary>
    /// 관문을 지나며 취소를 관측한다. 관문이 취소를 모르면 스크롤 취소가 관문이 열릴 때까지
    /// 지연되고, 그러면 "범위를 벗어난 요청이 실제로 풀리는가" 를 잴 수 없다.
    /// </summary>
    private async Task ArriveAsync(CancellationToken ct)
    {
        try
        {
            await ThumbnailGate.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            lock (gate)
            {
                CancellationsObserved++;
            }

            throw;
        }
    }

    private void ObserveCancellation(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            lock (gate)
            {
                CancellationsObserved++;
            }
        }

        ct.ThrowIfCancellationRequested();
    }
}
