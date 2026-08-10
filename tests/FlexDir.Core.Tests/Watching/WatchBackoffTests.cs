using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.Core.Tests.Watching;

/// <summary>
/// 감시 오버플로가 되풀이될 때의 대기 (docs/PRD-v2.md §13).
/// <para>
/// <b>고리를 끊는 것이 아니라 늦추는 것이다</b> (사용자 결정 2026-08-10). 오버플로에 대한
/// 처방은 전체 새로고침인데 (CLAUDE.md §4 — 진실원천은 파일시스템) <b>그 처방이 감시를
/// 다시 걸고, 다시 건 감시가 즉시 같은 오류를 낸다.</b> 처방과 증상이 한 고리에 있으므로
/// 대기가 없으면 반드시 폭주한다 — 실측으로 초당 약 10회였다.
/// </para>
/// </summary>
public class WatchBackoffTests
{
    [Fact]
    public void FirstOverflow_IsHandledRightAway()
    {
        // 한 번의 오버플로는 정상이다 (파일 100개 복사면 그것만으로 넘친다). 늦추면
        // 목록이 그만큼 오래 어긋난 채로 남는다.
        Assert.Equal(TimeSpan.Zero, WatchBackoff.Delay(0));
    }

    [Fact]
    public void RepeatedOverflows_WaitLongerEachTime()
    {
        var delays = Enumerable.Range(0, 6).Select(WatchBackoff.Delay).ToList();

        // 단조 증가여야 한다 — 어느 한 걸음이 짧아지면 그 자리에서 다시 폭주한다.
        Assert.Equal(delays, delays.Order());
        Assert.All(delays.Skip(1), delay => Assert.True(delay > TimeSpan.Zero));
    }

    [Fact]
    public void Waiting_StopsGrowingAtTheCeiling()
    {
        // 상한이 없으면 하루를 기다리는 상태가 된다 — 그것은 포기와 같고, 사용자는
        // 포기하지 않기로 정했다.
        Assert.Equal(WatchBackoff.Ceiling, WatchBackoff.Delay(50));
        Assert.Equal(WatchBackoff.Ceiling, WatchBackoff.Delay(int.MaxValue));
    }

    [Fact]
    public void NegativeSteps_AreTreatedAsTheFirst()
    {
        // 방어가 아니라 계산의 전체성이다 — 호출자가 세는 값이라 0 아래로 갈 이유가 없지만,
        // 여기서 던지면 오버플로 처리 경로가 예외로 죽는다.
        Assert.Equal(TimeSpan.Zero, WatchBackoff.Delay(-1));
    }

    [Fact]
    public void OverflowsCloseTogether_CountAsOneRun()
    {
        // 실측한 폭주는 간격이 100~200ms 였다.
        Assert.True(WatchBackoff.IsConsecutive(TimeSpan.FromMilliseconds(150)));
        Assert.True(WatchBackoff.IsConsecutive(TimeSpan.Zero));
    }

    [Fact]
    public void OverflowsFarApart_StartANewRun()
    {
        // 어제 한 번, 오늘 한 번은 폭주가 아니다. 이어 세면 정상 폴더에서도 대기가 쌓인다.
        Assert.False(WatchBackoff.IsConsecutive(WatchBackoff.Window + TimeSpan.FromMilliseconds(1)));
        Assert.False(WatchBackoff.IsConsecutive(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ATimeGoingBackwards_DoesNotStartANewRun()
    {
        // 시계가 뒤로 갈 수 있다 (시간 동기화). 음수 간격을 '멀다' 로 읽으면 그 순간
        // 대기가 0 으로 풀려 폭주가 되살아난다.
        Assert.True(WatchBackoff.IsConsecutive(TimeSpan.FromSeconds(-10)));
    }

    [Fact]
    public void Throttled_TurnsOnOnlyAfterItActuallyWaits()
    {
        // 상태표시줄에 내는 값이다. 한 번의 오버플로에도 켜지면 파일을 많이 복사할 때마다
        // "자동 갱신이 느려졌습니다" 가 뜬다.
        Assert.False(WatchBackoff.IsThrottled(0));
        Assert.True(WatchBackoff.IsThrottled(WatchBackoff.ThrottleAfter));
        Assert.True(WatchBackoff.IsThrottled(WatchBackoff.ThrottleAfter + 1));
    }

    [Fact]
    public void ThrottleThreshold_IsAboveTheFirstStep()
    {
        // 켜지는 시점이 첫 걸음이면 위 테스트의 의도가 무너진다.
        Assert.True(WatchBackoff.ThrottleAfter >= 1);
    }
}
