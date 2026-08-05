namespace FlexDir.App.Tests.Fakes;

/// <summary>
/// 손으로 감는 시계. <c>TypeAhead</c> 의 리셋 판정을 결정적으로 채점한다 —
/// 실제 시계로는 0.5초와 1.5초의 경계를 신뢰성 있게 만들 수 없다.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
