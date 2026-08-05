namespace FlexDir.App.Tests.Fakes;

/// <summary>
/// 손으로 감는 시계. <c>TypeAhead</c> 의 리셋 판정을 결정적으로 채점한다 —
/// 실제 시계로는 0.5초와 1.5초의 경계를 신뢰성 있게 만들 수 없다.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    // 경과 시간 계측(GetTimestamp/GetElapsedTime)도 이 시계를 따른다 — 기본 구현은
    // Stopwatch 라서 Advance 로 감을 수 없다. 주파수를 틱으로 맞추면 감은 만큼이 곧 경과다.
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => now.UtcTicks;

    public void Advance(TimeSpan delta) => now += delta;
}
