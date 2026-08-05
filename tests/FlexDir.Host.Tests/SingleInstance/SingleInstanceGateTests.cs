using FlexDir.Host.SingleInstance;

using Xunit;

namespace FlexDir.Host.Tests.SingleInstance;

/// <summary>
/// 두 번째 실행은 기존 프로세스에 인자를 넘기고 끝난다 (ADR-003 · docs/ARCHITECTURE.md §6).
/// <para>
/// 여기서는 <b>한 프로세스 안에서 두 게이트</b>로 잰다. 이름이 이름 있는 커널 객체를 가르므로
/// 프로세스 경계와 같은 판정을 받는다 — 진짜 두 번째 실행이 정말 첫 프로세스로 가는지는
/// 실물에서만 볼 수 있고, 그것은 <c>.harness/manual-plan.md</c> 의 사람 확인 항목이다.
/// </para>
/// <para>
/// 모든 대기에 시한을 준다. 파이프가 오지 않는 경우를 매달림이 아니라 실패로 만들어야
/// 진단이 된다 (.harness/HANDOFF.md §규칙 4).
/// </para>
/// </summary>
public class SingleInstanceGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>붙을 상대가 없는 경우를 재는 시한. 짧아야 그 테스트가 느려지지 않는다.</summary>
    private static readonly TimeSpan ShortConnect = TimeSpan.FromMilliseconds(300);

    private readonly string name = $"flex-dir-test-{Guid.NewGuid():N}";

    [Fact]
    public void Acquire_First_IsPrimary()
    {
        using var gate = SingleInstanceGate.Acquire(name);

        Assert.True(gate.IsPrimary);
    }

    [Fact]
    public void Acquire_Second_IsNotPrimary()
    {
        using var first = SingleInstanceGate.Acquire(name);
        using var second = SingleInstanceGate.Acquire(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
    }

    [Fact]
    public void Acquire_AfterThePrimaryIsGone_IsPrimaryAgain()
    {
        // 프로세스가 죽으면 다음 실행이 새 상주 프로세스가 되어야 한다. 이름이 남아 있으면
        // 앱이 두 번 다시 뜨지 않는다.
        SingleInstanceGate.Acquire(name).Dispose();

        using var next = SingleInstanceGate.Acquire(name);

        Assert.True(next.IsPrimary);
    }

    [Fact]
    public async Task SendAsync_ReachesThePrimary()
    {
        using var primary = SingleInstanceGate.Acquire(name);
        using var cts = new CancellationTokenSource(Timeout);

        var received = FirstActivationAsync(primary, cts.Token);

        using (var second = SingleInstanceGate.Acquire(name))
        {
            Assert.True(await second.SendAsync([@"C:\Temp"], Timeout, cts.Token));
        }

        Assert.Equal([@"C:\Temp"], await received.WaitAsync(Timeout));
    }

    [Fact]
    public async Task SendAsync_NoArguments_StillReachesThePrimary()
    {
        // 인자 없는 두 번째 실행은 "창을 다오" 라는 뜻이다. 무시하면 아이콘을 두 번 눌러도
        // 아무 일도 일어나지 않는다.
        using var primary = SingleInstanceGate.Acquire(name);
        using var cts = new CancellationTokenSource(Timeout);

        var received = FirstActivationAsync(primary, cts.Token);

        using (var second = SingleInstanceGate.Acquire(name))
        {
            Assert.True(await second.SendAsync([], Timeout, cts.Token));
        }

        Assert.Empty(await received.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ActivationsAsync_TwoRequests_ArriveInOrder()
    {
        // 상주 프로세스는 한 번만 활성화되는 것이 아니다. 첫 요청 뒤 서버를 다시 열지 않으면
        // 두 번째 실행부터 조용히 사라진다.
        using var primary = SingleInstanceGate.Acquire(name);
        using var cts = new CancellationTokenSource(Timeout);

        var received = new List<IReadOnlyList<string>>();
        var reading = Task.Run(
            async () =>
            {
                await foreach (var activation in primary.ActivationsAsync(cts.Token))
                {
                    received.Add(activation);

                    if (received.Count == 2)
                    {
                        return;
                    }
                }
            },
            cts.Token);

        using (var second = SingleInstanceGate.Acquire(name))
        {
            Assert.True(await second.SendAsync([@"C:\A"], Timeout, cts.Token));
            Assert.True(await second.SendAsync([@"C:\B"], Timeout, cts.Token));
        }

        await reading.WaitAsync(Timeout);

        Assert.Equal([@"C:\A"], received[0]);
        Assert.Equal([@"C:\B"], received[1]);
    }

    [Fact]
    public async Task SendAsync_WhenNobodyIsListening_IsFalse()
    {
        // 상주 프로세스가 정리 중이면 붙을 곳이 없다. 그때 두 번째 실행이 매달리면
        // 아이콘을 눌렀는데 아무 일도 없는 상태가 된다.
        using var primary = SingleInstanceGate.Acquire(name);
        using var second = SingleInstanceGate.Acquire(name);

        Assert.False(await second.SendAsync([@"C:\Temp"], ShortConnect, CancellationToken.None));
    }

    [Fact]
    public async Task ActivationsAsync_Cancelled_Ends()
    {
        using var primary = SingleInstanceGate.Acquire(name);
        using var cts = new CancellationTokenSource();

        var reading = Task.Run(
            async () =>
            {
                await foreach (var _ in primary.ActivationsAsync(cts.Token))
                {
                    // 취소로만 끝난다.
                }
            },
            CancellationToken.None);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ActivationsAsync_OnASecondary_Throws()
    {
        // 듣는 쪽은 상주 프로세스 하나뿐이다. 두 쪽이 들으면 활성화가 어디로 갈지 모른다.
        using var primary = SingleInstanceGate.Acquire(name);
        using var second = SingleInstanceGate.Acquire(name);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in second.ActivationsAsync(CancellationToken.None))
            {
                return;
            }
        });
    }

    [Fact]
    public async Task SendAsync_OnThePrimary_Throws()
    {
        using var primary = SingleInstanceGate.Acquire(name);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await primary.SendAsync([], Timeout, CancellationToken.None));
    }

    [Fact]
    public async Task Arguments_AreInvalid_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SingleInstanceGate.Acquire(null!));
        Assert.Throws<ArgumentException>(() => SingleInstanceGate.Acquire("  "));

        using var primary = SingleInstanceGate.Acquire(name);
        using var second = SingleInstanceGate.Acquire(name);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await second.SendAsync(null!, Timeout, CancellationToken.None));
    }

    /// <summary>첫 활성화 하나를 받고 끝나는 소비자. 뒤에 남는 반복자가 없어야 한다.</summary>
    private static Task<IReadOnlyList<string>> FirstActivationAsync(
        SingleInstanceGate gate,
        CancellationToken ct)
        => Task.Run(
            async () =>
            {
                await foreach (var activation in gate.ActivationsAsync(ct))
                {
                    return activation;
                }

                throw new InvalidOperationException("활성화가 오지 않았다.");
            },
            ct);
}
