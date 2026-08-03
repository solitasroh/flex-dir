using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IItemActivator"/> 의 기록용 fake. 아무것도 실행하지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 실제 구현체(<c>ShellExecuteEx</c>)는
/// <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009).
/// </para>
/// <para>
/// <see cref="Activations"/> 가 비어 있다는 단정문이 이 fake 의 주 용도다 —
/// 폴더 진입이 활성화 포트를 타면 shell 이 새 탐색기 창을 띄운다.
/// </para>
/// </summary>
public sealed class FakeItemActivator : IItemActivator
{
    /// <summary>활성화 요청이 들어온 항목을 순서대로 기록한다.</summary>
    public List<LocationId> Activations { get; } = [];

    /// <summary>활성화가 던질 예외. null 이면 성공한다.</summary>
    public LocationAccessException? Failure { get; set; }

    public int CancellationsObserved { get; private set; }

    public Task ActivateAsync(LocationId item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 실패해도 기록은 남긴다 — "불렸는데 실패했다" 와 "아예 안 불렸다" 는 다른 사건이다.
        Activations.Add(item);

        if (ct.IsCancellationRequested)
        {
            CancellationsObserved++;
        }

        ct.ThrowIfCancellationRequested();

        return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
}
