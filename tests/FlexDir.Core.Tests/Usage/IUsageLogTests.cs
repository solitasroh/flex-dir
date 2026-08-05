using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Usage;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// 실제 구현체(<c>FileUsageLog</c>)는 <c>FlexDir.Shell</c> 의 몫이다.
/// <para>
/// Host 테스트가 이 fake 로 재는 것은 <b>배선이 정말 불렸는가</b> 다 (실행·활성화). 그
/// 단정문의 근거가 <see cref="FakeUsageLog.Records"/> 이므로 기록이 먼저 믿을 만해야 한다.
/// </para>
/// </summary>
public class FakeUsageLogTests
{
    [Fact]
    public async Task RecordAsync_KeepsEveryCallInOrder()
    {
        var log = new FakeUsageLog();
        var first = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.FromHours(9));
        var second = first.AddHours(3);

        await log.RecordAsync(first, CancellationToken.None);
        await log.RecordAsync(second, CancellationToken.None);

        Assert.Equal([first, second], log.Records);
    }

    [Fact]
    public async Task RecordAsync_SameDayTwice_KeepsBoth()
    {
        // 하루 한 번으로 접는 것은 파일 구현체의 일이다 (게이트가 세는 것은 날짜다).
        var log = new FakeUsageLog();
        var at = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.FromHours(9));

        await log.RecordAsync(at, CancellationToken.None);
        await log.RecordAsync(at, CancellationToken.None);

        Assert.Equal(2, log.Records.Count);
    }

    [Fact]
    public async Task RecordAsync_Cancelled_Throws()
    {
        var log = new FakeUsageLog();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await log.RecordAsync(DateTimeOffset.UnixEpoch, cts.Token));

        Assert.Empty(log.Records);
    }
}
