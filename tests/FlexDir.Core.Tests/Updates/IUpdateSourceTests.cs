using FlexDir.Core.Updates;

using Xunit;

namespace FlexDir.Core.Tests.Updates;

/// <summary>
/// 자동 업데이트 포트의 계약 (docs/PRD-v2.md §9).
/// <para>
/// <b>여기서 채점하는 것은 값의 규칙뿐이다.</b> 실제 갱신은 프로세스를 교체하므로
/// 자동 테스트가 밟을 수 없다 (CLAUDE.md §5) — 실물은 사람이 본다.
/// 그래서 포트를 <b>확인 · 받기 · 적용</b> 셋으로 쪼갰다: 앞의 둘만 값으로 판정된다.
/// </para>
/// </summary>
public class IUpdateSourceTests
{
    // ── 새 버전의 표현 ────────────────────────────────────────────

    [Fact]
    public void AvailableUpdate_KeepsTheVersionVerbatim()
    {
        // 화면에 그대로 나가는 문자열이다. 다듬으면 사용자가 릴리스 목록과 대조할 수 없다.
        Assert.Equal("1.2.3", new AvailableUpdate("1.2.3").Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AvailableUpdate_WithoutAVersion_Throws(string version)
    {
        // 버전 없는 '새 버전' 은 사용자에게 아무것도 말하지 못한다. 빈 값으로 두면
        // 알림 바가 "새 버전  — 지금 설치" 로 뜬다.
        Assert.Throws<ArgumentException>(() => new AvailableUpdate(version));
    }

    [Fact]
    public void AvailableUpdate_WithNullVersion_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AvailableUpdate(null!));
    }

    // ── 없음은 null 이다 ──────────────────────────────────────────

    [Fact]
    public async Task Check_WhenThereIsNothingNew_IsNull()
    {
        // "없음" 을 빈 버전으로 말하지 않는다 — IDriveSpace 가 모르는 용량을 null 로 내는
        // 것과 같은 규칙이다. 있음/없음을 호출자가 한 번에 본다.
        var source = new NothingNew();

        Assert.Null(await source.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_ObservesCancellation()
    {
        // 네트워크에 닿는다 — 창을 닫거나 종료하면 곧바로 놓아야 한다 (CLAUDE.md §3).
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new NothingNew().CheckAsync(cancelled.Token));
    }

    /// <summary>확인이 아무것도 못 찾은 경우. 계약이 요구하는 모양만 갖춘다.</summary>
    private sealed class NothingNew : IUpdateSource
    {
        public Task<AvailableUpdate?> CheckAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return Task.FromResult<AvailableUpdate?>(null);
        }

        public Task DownloadAsync(AvailableUpdate update, CancellationToken ct) => Task.CompletedTask;

        public void ApplyAndRestart(AvailableUpdate update)
        {
        }
    }
}
