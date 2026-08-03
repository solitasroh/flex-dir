using System.Runtime.CompilerServices;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IFolderSource"/> 의 사전 기반 fake. 파일시스템을 읽지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 실제 구현체(<c>FindFirstFileEx</c>)는
/// <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009).
/// </para>
/// <para>
/// 열거·ViewModel 테스트가 이 fake 를 주입해 쓴다. 지연·중간 실패·취소 관측을
/// 넣을 수 있는 이유가 그것이다 — 실제 구현체에서만 일어나는 일을 자동 검증
/// 대상으로 끌어오려면 fake 가 그 상황을 재현할 수 있어야 한다.
/// </para>
/// </summary>
public sealed class FakeFolderSource : IFolderSource
{
    /// <summary>폴더별 항목 목록. 등록되지 않은 폴더는 <see cref="LocationErrorKind.NotFound"/> 다.</summary>
    public Dictionary<LocationId, List<FileItem>> Folders { get; } = [];

    /// <summary>항목을 하나 낼 때마다 이 만큼 await 한다. 0 이면 즉시.</summary>
    public int YieldDelayMilliseconds { get; set; }

    /// <summary>N 번째 항목을 낸 뒤 예외를 던진다. null 이면 던지지 않는다.</summary>
    public (int AfterItems, LocationErrorKind Kind)? FailureInjection { get; set; }

    /// <summary><see cref="EnumerateAsync"/> 가 호출된 폴더의 순서. 취소·격리 검증에 쓴다.</summary>
    public List<LocationId> EnumerateCalls { get; } = [];

    /// <summary>취소가 관측된 횟수.</summary>
    public int CancellationsObserved { get; private set; }

    public IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // 호출 시점에 기록한다. 반복자 본문은 첫 MoveNextAsync 까지 실행되지 않으므로
        // 안에서 기록하면 "호출했는가" 가 아니라 "소비를 시작했는가" 를 재는 것이 된다.
        EnumerateCalls.Add(folder);

        return Enumerate(folder, ct);
    }

    public Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObserveCancellation(ct);

        // 부모를 유도해 폴더를 찾지 않고 등록된 목록 전체를 훑는다 — 드라이브 루트의
        // 항목(부모가 없는 경우)까지 같은 경로로 다뤄진다.
        var found = Folders.Values
            .SelectMany(items => items)
            .FirstOrDefault(candidate => candidate.Location.Equals(item));

        return Task.FromResult<FileItem?>(found);
    }

    private async IAsyncEnumerable<FileItem> Enumerate(
        LocationId folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObserveCancellation(ct);

        if (!Folders.TryGetValue(folder, out var items))
        {
            throw new LocationAccessException(LocationErrorKind.NotFound, folder);
        }

        var yielded = 0;

        foreach (var item in items)
        {
            ThrowIfInjected(yielded, folder);

            // 항목마다 관측한다. 실제 구현체도 그래야 하므로 fake 가 먼저 계약을 강제한다.
            ObserveCancellation(ct);

            if (YieldDelayMilliseconds > 0)
            {
                await Task.Delay(YieldDelayMilliseconds, ct);
            }

            yield return item;
            yielded++;
        }

        // 마지막 항목 뒤의 주입도 살린다 — 열거가 끝나기 직전에 폴더가 사라지는 경우다.
        ThrowIfInjected(yielded, folder);
    }

    private void ThrowIfInjected(int yielded, LocationId folder)
    {
        if (FailureInjection is { } failure && yielded == failure.AfterItems)
        {
            throw new LocationAccessException(failure.Kind, folder);
        }
    }

    private void ObserveCancellation(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            CancellationsObserved++;
        }

        ct.ThrowIfCancellationRequested();
    }
}
