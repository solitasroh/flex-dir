namespace FlexDir.Core.Watching;

/// <summary>
/// 감시 오버플로가 되풀이될 때 얼마나 기다렸다 다시 읽을 것인가 (docs/PRD-v2.md §13).
///
/// <para>
/// <b>왜 필요한가</b>: 오버플로(<see cref="FolderChange.Overflowed"/>)에 대한 처방은 전체
/// 새로고침이다 — 유실된 개별 변경을 흉내낼 수 없으므로 파일시스템을 다시 믿는다
/// (CLAUDE.md §4). 그런데 <b>그 처방이 감시를 다시 걸고, 다시 건 감시가 즉시 같은 오류를
/// 낸다.</b> 처방과 증상이 한 고리에 있어 대기가 없으면 반드시 폭주한다.
/// </para>
///
/// <para>
/// <b>실측</b> (2026-08-10 · <c>\\wsl.localhost\Ubuntu-26.04\etc</c>): 12초에 재열거 123회 ·
/// 오버플로 109회. UI 스레드가 그 갱신에 매여 선택 조작이 먹히지 않았다. WSL 전용이 아니라
/// <b>감시가 즉시 실패하는 모든 경로</b>에서 같은 일이 난다.
/// </para>
///
/// <para>
/// <b>포기하지 않는다</b> (사용자 결정 2026-08-10). 간격을 <see cref="Ceiling"/> 까지만
/// 벌리고 계속 시도한다 — 일시적인 문제면 저절로 회복하고, 지속되면 대기가 상한에 머문다.
/// 대신 그동안 <see cref="IsThrottled"/> 로 사용자에게 알린다: 목록이 파일시스템과 어긋날
/// 수 있다는 것은 손쓸 수 있는(새로 고침) 사실이라 조용히 두면 안 된다.
/// </para>
///
/// <para>
/// 순수 계산이라 <c>Core</c> 에 있다 — 시각을 읽지 않고 <b>간격</b>을 받는다. 그래야 규칙이
/// 전부 자동 채점되고, 시계를 쥐는 쪽(<c>PaneViewModel</c> 의 <c>TimeProvider</c>)과 갈리지
/// 않는다.
/// </para>
/// </summary>
public static class WatchBackoff
{
    /// <summary>
    /// 이 간격 안에 다시 온 오버플로는 <b>같은 폭주</b>로 센다. 실측한 폭주는 100~200ms
    /// 간격이었고, 정상적인 대량 복사가 내는 오버플로는 이보다 훨씬 드물다.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 대기의 상한. 없으면 몇 걸음 만에 하루가 되고 그것은 포기와 같다.
    /// </summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    /// <summary>첫 대기. 이후 걸음마다 두 배가 된다.</summary>
    private static readonly TimeSpan FirstStep = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 이 걸음부터 사용자에게 알린다. 한 번의 오버플로는 정상이므로 (파일 100개를 한 번에
    /// 복사하면 그것만으로 넘친다) 곧바로 켜지면 소음이 된다.
    /// </summary>
    public const int ThrottleAfter = 2;

    /// <summary>
    /// <paramref name="consecutive"/> 번째 연속 오버플로에서 기다릴 시간.
    /// 첫 번째(0)는 기다리지 않는다 — 한 번의 오버플로는 정상이고, 늦추면 목록이 그만큼
    /// 오래 어긋난 채로 남는다.
    /// </summary>
    public static TimeSpan Delay(int consecutive)
    {
        if (consecutive <= 0)
        {
            return TimeSpan.Zero;
        }

        // 지수로 늘리되 곱셈이 넘치지 않게 걸음 수를 먼저 자른다. Ceiling / FirstStep 이
        // 64 를 넘지 않는 한 여섯 걸음이면 이미 상한이다.
        var steps = Math.Min(consecutive - 1, 16);
        var ticks = FirstStep.Ticks * (1L << steps);

        return ticks >= Ceiling.Ticks ? Ceiling : TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// 직전 오버플로와 이어지는가. <paramref name="sinceLast"/> 는 그 사이의 간격이다.
    /// <para>
    /// <b>음수도 '이어진다' 로 본다.</b> 시계가 뒤로 갈 수 있고(시간 동기화), 그것을 '멀다'
    /// 로 읽으면 그 순간 대기가 0 으로 풀려 폭주가 되살아난다.
    /// </para>
    /// </summary>
    public static bool IsConsecutive(TimeSpan sinceLast) => sinceLast <= Window;

    /// <summary>
    /// 사용자에게 알릴 만큼 되풀이됐는가. 상태표시줄이 이 값을 본다.
    /// </summary>
    public static bool IsThrottled(int consecutive) => consecutive >= ThrottleAfter;
}
