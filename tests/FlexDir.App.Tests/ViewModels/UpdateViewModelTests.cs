using System.IO;
using System.Net.Http;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Updates;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 새 버전 알림 (docs/PRD-v2.md §9 · 사용자 결정 2026-08-07: 뜨고 사용자가 고른다).
/// <para>
/// <b>적용은 여기서 채점하지 않는다</b> — 프로세스를 교체하므로 fake 가 "불렸다" 만 센다.
/// 실제로 갱신되는지는 사람이 본다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class UpdateViewModelTests
{
    private readonly FakeUpdateSource updates = new();
    private readonly InlineUiDispatcher dispatcher = new();

    private UpdateViewModel Create() => new(updates, dispatcher);

    // ── 없을 때는 조용하다 ────────────────────────────────────────

    [Fact]
    public async Task Check_WhenThereIsNothingNew_StaysSilent()
    {
        var update = Create();

        await update.CheckAsync(CancellationToken.None);

        Assert.False(update.IsAvailable);
        Assert.Null(update.Version);
    }

    [Fact]
    public async Task Check_WhenTheFeedCannotBeReached_StaysSilent()
    {
        // 사내망 밖이거나 서버가 꺼져 있는 것은 정상 상황이다. 오류를 띄우면 매 실행마다
        // 사용자가 손쓸 수 없는 알림을 본다 — 업데이트는 사용자가 요구한 작업이 아니다.
        updates.Failure = new HttpRequestException("피드에 못 닿는다");
        var update = Create();

        await update.CheckAsync(CancellationToken.None);

        Assert.False(update.IsAvailable);
    }

    // ── 있을 때는 받아 두고 알린다 ────────────────────────────────

    [Fact]
    public async Task Check_WhenThereIsANewVersion_DownloadsThenAnnounces()
    {
        updates.Available = new AvailableUpdate("1.2.3");
        var update = Create();

        await update.CheckAsync(CancellationToken.None);

        Assert.True(update.IsAvailable);
        Assert.Equal("1.2.3", update.Version);

        // 받아 둔 뒤에 알린다 — 누르고 나서 받으면 '지금 설치' 가 몇 분짜리 조작이 된다.
        Assert.Equal(["1.2.3"], updates.Downloads.Select(item => item.Version));
    }

    [Fact]
    public async Task Check_WhileTheDownloadIsRunning_DoesNotAnnounceYet()
    {
        updates.Available = new AvailableUpdate("1.2.3");
        updates.DownloadGate = new TaskCompletionSource();
        var update = Create();

        var checking = update.CheckAsync(CancellationToken.None);

        // 아직 받는 중이다. 여기서 알리면 '지금 설치' 가 눌려도 적용할 것이 없다.
        Assert.False(update.IsAvailable);

        updates.DownloadGate.SetResult();
        await checking;

        Assert.True(update.IsAvailable);
    }

    [Fact]
    public async Task Check_WhenTheDownloadFails_StaysSilent()
    {
        updates.Available = new AvailableUpdate("1.2.3");
        updates.Failure = new IOException("받다 끊겼다");
        var update = Create();

        await update.CheckAsync(CancellationToken.None);

        Assert.False(update.IsAvailable);
    }

    // ── 사용자가 고른다 ───────────────────────────────────────────

    [Fact]
    public async Task Install_AppliesTheUpdate()
    {
        updates.Available = new AvailableUpdate("1.2.3");
        var update = Create();
        await update.CheckAsync(CancellationToken.None);

        update.InstallCommand.Execute(null);

        Assert.Equal(["1.2.3"], updates.Applied.Select(item => item.Version));
    }

    [Fact]
    public void Install_WithNothingAvailable_DoesNothing()
    {
        var update = Create();

        update.InstallCommand.Execute(null);

        Assert.Empty(updates.Applied);
    }

    [Fact]
    public async Task Dismiss_HidesTheNoticeWithoutApplying()
    {
        // '나중에' 는 거절이 아니라 미루기다. 적용하지 않고 알림만 접는다 —
        // 다음 실행의 확인이 같은 버전을 다시 찾는다.
        updates.Available = new AvailableUpdate("1.2.3");
        var update = Create();
        await update.CheckAsync(CancellationToken.None);

        update.DismissCommand.Execute(null);

        Assert.False(update.IsAvailable);
        Assert.Empty(updates.Applied);
    }

    [Fact]
    public async Task Dismiss_ThenInstall_DoesNotApply()
    {
        // 접은 뒤에는 누를 자리가 없다. 커맨드가 살아 있으면 화면과 상태가 어긋난다.
        updates.Available = new AvailableUpdate("1.2.3");
        var update = Create();
        await update.CheckAsync(CancellationToken.None);
        update.DismissCommand.Execute(null);

        update.InstallCommand.Execute(null);

        Assert.Empty(updates.Applied);
    }

    // ── 취소 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Check_WhenCancelled_StaysSilentAndDoesNotThrow()
    {
        // 종료 경로가 이것을 취소한다. 예외로 나가면 아무도 기다리지 않는 Task 가 faulted 로 남는다.
        updates.Available = new AvailableUpdate("1.2.3");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var update = Create();

        await update.CheckAsync(cancelled.Token);

        Assert.False(update.IsAvailable);
    }
}
