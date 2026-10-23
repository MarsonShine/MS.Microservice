using System;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Core.Common.Advance.Resilience;
using MS.Microservice.Core.Common.Advance.Resilience.RetryCondition;
using MS.Microservice.Core.Common.Advance.Resilience.RetryStrategy;
using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Common.Advance.Resilience;

public class RetryDelayBoundaryTests
{
    [Theory]
    [InlineData(double.MaxValue, 2)]
    [InlineData(double.MaxValue, 3)]
    [InlineData(2.0, int.MaxValue)]
    public void OverflowingExponentialDelay_ReturnsCap(double multiplier, int attempt)
    {
        var cap = TimeSpan.FromMinutes(1);
        var strategy = new ExponentialBackoffRetryStrategy(int.MaxValue, TimeSpan.FromSeconds(1), multiplier, cap);

        Assert.Equal(cap, strategy.GetDelay(new RetryContext { Attempt = attempt }));
    }

    [Fact]
    public void ZeroBaseDelay_RemainsZeroWhenExponentOverflows()
    {
        var strategy = new ExponentialBackoffRetryStrategy(int.MaxValue, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, strategy.GetDelay(new RetryContext { Attempt = int.MaxValue }));
    }

    [Fact]
    public void DefaultCap_RemainsFiveMinutesWhileExplicitMaximumAllowsLongerDelay()
    {
        var bounded = new ExponentialBackoffRetryStrategy(10, TimeSpan.FromSeconds(1));
        var unbounded = new ExponentialBackoffRetryStrategy(10, TimeSpan.FromSeconds(1), maxDelay: TimeSpan.MaxValue);
        var context = new RetryContext { Attempt = 10 };

        Assert.Equal(TimeSpan.FromMinutes(5), bounded.GetDelay(context));
        Assert.Equal(TimeSpan.FromSeconds(512), unbounded.GetDelay(context));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TimeSpanMaximum_IsReturnedWithoutConversionOverflow(int attempt)
    {
        var strategy = new ExponentialBackoffRetryStrategy(2, TimeSpan.MaxValue, maxDelay: TimeSpan.MaxValue);

        Assert.Equal(TimeSpan.MaxValue, strategy.GetDelay(new RetryContext { Attempt = attempt }));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.5, 500.0, 250.0)]
    public void DecreasingMultiplier_PreservesDelaySequence(double multiplier, double secondMilliseconds, double thirdMilliseconds)
    {
        var strategy = new ExponentialBackoffRetryStrategy(3, TimeSpan.FromSeconds(1), multiplier);

        Assert.Equal(TimeSpan.FromSeconds(1), strategy.GetDelay(new RetryContext { Attempt = 1 }));
        Assert.Equal(TimeSpan.FromMilliseconds(secondMilliseconds), strategy.GetDelay(new RetryContext { Attempt = 2 }));
        Assert.Equal(TimeSpan.FromMilliseconds(thirdMilliseconds), strategy.GetDelay(new RetryContext { Attempt = 3 }));
    }

    [Fact]
    public async Task DelayLargerThanTimerLimit_RemainsCancellableWithoutRetryingEarly()
    {
        using var cancellation = new CancellationTokenSource();
        var reachedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new RetryExecutor(new MaximumDelayStrategy(reachedDelay),
            new ExceptionTypeRetryCondition(typeof(InvalidOperationException)));
        var attempts = 0;
        var execution = executor.ExecuteAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("retry");
        }, cancellation.Token);

        try
        {
            await reachedDelay.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
        }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public async Task NegativeFunctionalDelay_IsRejectedBeforeCallingOperation(int delayMillis)
    {
        var attempts = 0;
        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => F.Retry(1, delayMillis, () =>
        {
            attempts++;
            return Task.FromResult(42);
        }));

        Assert.Equal("delayMillis", error.ParamName);
        Assert.Equal(0, attempts);
    }

    private sealed class MaximumDelayStrategy(TaskCompletionSource reachedDelay) : IRetryStrategy
    {
        public bool ShouldRetry(RetryContext context) => context.Attempt <= 1;

        public TimeSpan GetDelay(RetryContext context)
        {
            reachedDelay.TrySetResult();
            return TimeSpan.MaxValue;
        }
    }
}
