using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// UI 스레드 예외를 살아남는 자리 (docs/PRD-v2.md §14).
/// <para>
/// <b>여기서 채점하는 것은 판정과 문구뿐이다.</b> 실제로 <c>Application.Handled</c> 에
/// 무엇이 걸리는지는 <c>Host/Program</c> 의 배선이고, 그것이 정말 프로세스를 살리는지는
/// 사람이 본다 (CLAUDE.md §5).
/// </para>
/// <para>
/// 한도 판정은 주입한 <see cref="TimeProvider"/> 로만 한다 — 실제 시계로는 1분 경계를
/// 결정적으로 만들 수 없다 (<c>PaneTypeAheadTests</c> 와 같은 이유·같은 fake).
/// </para>
/// </summary>
public class CrashNoticeViewModelTests
{
    private readonly ManualTimeProvider clock = new();

    private CrashNoticeViewModel Create() => new(clock);

    // ── 아무 일도 없으면 접혀 있다 ────────────────────────────────

    [Fact]
    public void Initially_IsSilent()
    {
        var notice = Create();

        Assert.False(notice.IsVisible);
        Assert.Null(notice.Message);
    }

    // ── 한 번 나면 삼키고 한 번 말한다 ───────────────────────────

    [Fact]
    public void Report_TheFirstTime_RecoversAndAnnounces()
    {
        var notice = Create();

        var recovered = notice.Report(new InvalidOperationException("우클릭 한 번에 죽었다"));

        Assert.True(recovered);
        Assert.True(notice.IsVisible);
        Assert.NotNull(notice.Message);
    }

    [Fact]
    public void Report_NamesTheException()
    {
        // 사용자 결정 2026-08-11: 예외 이름까지 드러낸다. 익숙한 이름이면 그 한 줄로
        // 다음 행동이 갈리고, 아니면 로그를 열면 된다.
        var notice = Create();

        notice.Report(new InvalidOperationException("x"));

        Assert.Contains(nameof(InvalidOperationException), notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_NamesTheActualException_NotAFixedOne()
    {
        // 이름을 하드코딩해 두면 어떤 예외가 나도 같은 문구가 뜬다 — 그러면 드러낸
        // 의미가 없다.
        var notice = Create();

        notice.Report(new UnauthorizedAccessException("x"));

        Assert.Contains(nameof(UnauthorizedAccessException), notice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_PointsAtTheLogFile()
    {
        // 바 한 줄에 스택을 담을 수 없다. 어디를 열면 되는지가 없으면 사용자는 예외
        // 이름만 들고 아무 데도 못 간다.
        var notice = Create();

        notice.Report(new InvalidOperationException("x"));

        Assert.Contains(CrashNoticeViewModel.LogFileName, notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_Again_ReplacesTheMessage()
    {
        // 바는 하나다. 두 번째 사고가 첫 번째 문구 뒤에 쌓이면 그 자리가 로그가 된다.
        var notice = Create();

        notice.Report(new InvalidOperationException("x"));
        notice.Report(new UnauthorizedAccessException("y"));

        Assert.Contains(nameof(UnauthorizedAccessException), notice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), notice.Message, StringComparison.Ordinal);
    }

    // ── 접으면 조용해진다 ─────────────────────────────────────────

    [Fact]
    public void Dismiss_FoldsTheBar()
    {
        var notice = Create();
        notice.Report(new InvalidOperationException("x"));

        notice.DismissCommand.Execute(null);

        Assert.False(notice.IsVisible);
        Assert.Null(notice.Message);
    }

    [Fact]
    public void Report_AfterDismiss_AnnouncesAgain()
    {
        // 접은 것은 "봤다" 이지 "그만 알려라" 가 아니다.
        var notice = Create();
        notice.Report(new InvalidOperationException("x"));
        notice.DismissCommand.Execute(null);

        var recovered = notice.Report(new InvalidOperationException("x"));

        Assert.True(recovered);
        Assert.True(notice.IsVisible);
    }

    [Fact]
    public void Dismiss_DoesNotRefillTheBudget()
    {
        // 접기가 한도를 되돌리면 폭주 중에 바를 닫는 것만으로 좀비가 된다 —
        // 그리고 폭주 중에는 바가 계속 떠서 사용자가 계속 닫게 된다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            notice.Report(new InvalidOperationException("x"));
            notice.DismissCommand.Execute(null);
        }

        Assert.False(notice.Report(new InvalidOperationException("x")));
    }

    // ── 폭주는 살리지 않는다 ─────────────────────────────────────

    [Fact]
    public void Report_UpToTheLimit_KeepsRecovering()
    {
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            Assert.True(notice.Report(new InvalidOperationException("x")), $"{i + 1}번째");
        }
    }

    [Fact]
    public void Report_PastTheLimit_GivesUp()
    {
        // LayoutUpdated 같은 자리의 버그는 매 프레임 터진다. 무조건 삼키면 앱이 죽는
        // 대신 예외를 쏟아내며 느려지고, 증상이 더 안 보이는 모양이 된다 — §13 이
        // 겪은 "처방이 증상을 다시 만드는 고리" 와 같다. 고칠 수 없으면 오늘처럼 죽는다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            notice.Report(new InvalidOperationException("x"));
        }

        Assert.False(notice.Report(new InvalidOperationException("x")));
    }

    [Fact]
    public void Report_PastTheLimit_LeavesTheBarAlone()
    {
        // 포기했으면 프로세스가 곧 죽는다. 그 직전에 문구를 바꿔 봐야 볼 사람이 없고,
        // 마지막으로 보인 문구가 남는 편이 낫다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            notice.Report(new InvalidOperationException("x"));
        }

        notice.Report(new UnauthorizedAccessException("마지막"));

        Assert.DoesNotContain(nameof(UnauthorizedAccessException), notice.Message, StringComparison.Ordinal);
    }

    // ── 창이 지나면 잊는다 ────────────────────────────────────────

    [Fact]
    public void Report_AfterTheWindowPasses_RecoversAgain()
    {
        // 한도는 "폭주인가" 를 재는 것이지 프로세스의 평생 예산이 아니다. 아침에 한 번,
        // 오후에 한 번 난 것이 저녁의 한 번을 죽이면 안 된다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            notice.Report(new InvalidOperationException("x"));
        }

        clock.Advance(CrashNoticeViewModel.RecoveryWindow + TimeSpan.FromSeconds(1));

        Assert.True(notice.Report(new InvalidOperationException("x")));
    }

    [Fact]
    public void Report_SpreadOverTime_NeverGivesUp()
    {
        // 창보다 느리게 나는 것은 폭주가 아니다. 몇 번을 반복해도 계속 살아난다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries * 3; i++)
        {
            Assert.True(notice.Report(new InvalidOperationException("x")), $"{i + 1}번째");

            clock.Advance(CrashNoticeViewModel.RecoveryWindow + TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void Report_WhenOnlyPartOfTheWindowPasses_StillCounts()
    {
        // 창을 "마지막 사고로부터" 로 재면 4분 59초마다 한 번씩 나는 폭주가 영원히
        // 살아난다. 재는 것은 각 사고의 시각이다.
        var notice = Create();

        for (var i = 0; i < CrashNoticeViewModel.MaxRecoveries; i++)
        {
            notice.Report(new InvalidOperationException("x"));

            clock.Advance(CrashNoticeViewModel.RecoveryWindow / 10);
        }

        Assert.False(notice.Report(new InvalidOperationException("x")));
    }

    // ── 계약 ──────────────────────────────────────────────────────

    [Fact]
    public void Report_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => Create().Report(null!));

    [Fact]
    public void Limits_AreTheCodeSideSourceOfTruth()
    {
        // 수치가 문서와 코드 두 곳에 있으면 갈린다 — PerformanceLog.Budget 과 같은 자리다.
        // docs/PRD-v2.md §14 가 그 근거다.
        Assert.Equal(5, CrashNoticeViewModel.MaxRecoveries);
        Assert.Equal(TimeSpan.FromMinutes(1), CrashNoticeViewModel.RecoveryWindow);
    }
}
