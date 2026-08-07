using FlexDir.Core.Updates;
using FlexDir.Shell.Updates;

using Xunit;

namespace FlexDir.Shell.Tests.Updates;

/// <summary>
/// Velopack 을 <see cref="IUpdateSource"/> 에 끼운 구현체 (docs/PRD-v2.md §9).
///
/// <para>
/// <b>실물 피드에 닿지 않는다.</b> 네트워크가 필요한 테스트는 사내망 밖에서 조용히
/// 빨간불이 되고, 실제 갱신은 프로세스를 교체하므로 실행기가 사라진다 (CLAUDE.md §5).
/// 여기서 고정하는 것은 <b>설치되지 않은 상태에서의 규칙</b>이다 — 그것이 개발 중 매
/// 실행에서 밟히는 유일한 경로이고, 틀리면 알림이 계속 뜨거나 예외가 샌다.
/// </para>
/// </summary>
public class VelopackUpdateSourceTests
{
    private const string Feed = "https://github.com/solitasroh/flex-dir";

    // ── 설치되지 않은 상태 ────────────────────────────────────────
    // 테스트 실행기도 dotnet run 도 여기 해당한다. 이 경로가 예외를 내면 게이트가 깨진다.

    [Fact]
    public async Task Check_WhenNotInstalled_IsNull()
    {
        // 업데이트 기제가 없는 실행이다. 예외가 아니라 "새 버전 없음" 이어야 한다 —
        // 오류로 만들면 개발 중 매 실행마다 손쓸 수 없는 알림이 뜬다.
        Assert.Null(await new VelopackUpdateSource(Feed).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_WhenNotInstalled_DoesNotTouchTheNetwork()
    {
        // 설치 여부를 먼저 보고 물러난다. 네트워크를 먼저 때리면 피드가 느릴 때
        // 개발 중 실행이 그만큼 늦어진다 (CLAUDE.md §3).
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        await new VelopackUpdateSource(Feed).CheckAsync(CancellationToken.None);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"{elapsed.Elapsed} 걸렸다");
    }

    [Fact]
    public async Task Check_ObservesCancellationBeforeAnythingElse()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new VelopackUpdateSource(Feed).CheckAsync(cancelled.Token));
    }

    // ── 피드 주소 ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithoutAFeed_Throws(string feed)
    {
        // 피드 없는 업데이트 원본은 아무것도 못 한다. 조용히 '없음' 으로 접으면
        // 배선을 빠뜨린 것이 영영 드러나지 않는다.
        Assert.Throws<ArgumentException>(() => new VelopackUpdateSource(feed));
    }

    [Fact]
    public void Constructor_WithNullFeed_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VelopackUpdateSource(null!));
    }

    // ── 계약을 어기지 않는다 ──────────────────────────────────────

    [Fact]
    public async Task Download_WithoutAnUpdate_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new VelopackUpdateSource(Feed).DownloadAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Apply_WithoutAnUpdate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new VelopackUpdateSource(Feed).ApplyAndRestart(null!));
    }

    [Fact]
    public async Task Download_ForSomethingThatWasNeverChecked_Throws()
    {
        // 확인이 낸 것만 받을 수 있다. 임의의 버전을 받으라고 하면 Velopack 이 쥔
        // UpdateInfo 가 없어 적용 대상이 정해지지 않는다 — 조용히 성공하면 '지금 설치'
        // 가 아무 일도 하지 않는다.
        var source = new VelopackUpdateSource(Feed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.DownloadAsync(new AvailableUpdate("9.9.9"), CancellationToken.None));
    }
}
