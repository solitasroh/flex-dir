using FlexDir.Core.Usage;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IUsageLog"/> 의 기록용 fake. 파일을 건드리지 않는다.
/// <para>
/// <b>하루 한 번으로 접지 않는다.</b> 그것은 파일 구현체의 일이고 (게이트가 세는 것은 날짜다),
/// 여기서 접으면 "배선이 정말 불렸는가" 를 셀 수 없다 — 이 fake 를 쓰는 Host 테스트가 재는
/// 것이 바로 그것이다.
/// </para>
/// </summary>
public sealed class FakeUsageLog : IUsageLog
{
    private readonly object gate = new();

    /// <summary>들어온 기록. 순서대로 남는다.</summary>
    public List<DateTimeOffset> Records { get; } = [];

    public ValueTask RecordAsync(DateTimeOffset at, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (gate)
        {
            Records.Add(at);
        }

        return ValueTask.CompletedTask;
    }
}
