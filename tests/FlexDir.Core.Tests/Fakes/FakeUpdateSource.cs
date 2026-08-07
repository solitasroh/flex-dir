using FlexDir.Core.Updates;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IUpdateSource"/> 의 fake. <c>FlexDir.App.Tests</c> 가 이 프로젝트를 참조해
/// 재사용한다 (CLAUDE.md §6-5).
/// <para>
/// <b>적용은 실제로 하지 않고 세었다는 사실만 남긴다.</b> 진짜 구현은 프로세스를 교체하므로
/// 테스트가 그것을 부르면 실행기가 사라진다 — 자동으로 밟을 수 없는 자리다 (CLAUDE.md §5).
/// </para>
/// </summary>
public sealed class FakeUpdateSource : IUpdateSource
{
    /// <summary>확인이 낼 것. null 이면 "새 버전 없음" 이다.</summary>
    public AvailableUpdate? Available { get; set; }

    /// <summary>
    /// 확인·받기가 던질 예외. 피드에 못 닿는 상황(사내망 밖·서버 꺼짐)이 정상 상황이라
    /// <b>그때 무엇이 보이는가</b>를 테스트가 만들 수 있어야 한다.
    /// </summary>
    public Exception? Failure { get; set; }

    public int Checks { get; private set; }

    public List<AvailableUpdate> Downloads { get; } = [];

    public List<AvailableUpdate> Applied { get; } = [];

    /// <summary>받기가 끝나는 시점을 테스트가 쥔다. null 이면 곧바로 끝난다.</summary>
    public TaskCompletionSource? DownloadGate { get; set; }

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Checks++;

        return Failure is { } failure
            ? Task.FromException<AvailableUpdate?>(failure)
            : Task.FromResult(Available);
    }

    public async Task DownloadAsync(AvailableUpdate update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        ct.ThrowIfCancellationRequested();

        if (Failure is { } failure)
        {
            throw failure;
        }

        if (DownloadGate is { } gate)
        {
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        Downloads.Add(update);
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        Applied.Add(update);
    }
}
