using MS.Microservice.Core.Common.Advance.Resilience;
using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience;

public sealed class RetryContextTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void TimeoutUsesElapsedTimestampDespiteForwardAndBackwardUtcChanges(int utcJumpDays)
    {
        var clock = new IndependentClock();
        var startedAt = clock.GetUtcNow().UtcDateTime;
        var context = new RetryContext(clock);
        var strategy = new TimeoutRetryStrategy(TimeSpan.FromSeconds(1));

        clock.UtcNow = clock.UtcNow.AddDays(utcJumpDays);
        Assert.Equal(TimeSpan.Zero, context.TotalElapsed);
        Assert.True(strategy.ShouldRetry(context));

        clock.Timestamp += 999;
        Assert.Equal(TimeSpan.FromMilliseconds(999), context.TotalElapsed);
        Assert.True(strategy.ShouldRetry(context));

        clock.UtcNow = clock.UtcNow.AddDays(-2 * utcJumpDays);
        Assert.Equal(TimeSpan.FromMilliseconds(999), context.TotalElapsed);
        clock.Timestamp++;
        Assert.Equal(TimeSpan.FromSeconds(1), context.TotalElapsed);
        Assert.False(strategy.ShouldRetry(context));
        Assert.Equal(startedAt, context.StartTime);
        Assert.Equal(DateTimeKind.Utc, context.StartTime.Kind);
    }

    private sealed class IndependentClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public long Timestamp { get; set; } = 42_000;
        public override long TimestampFrequency => 1000;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => Timestamp;
    }
}
