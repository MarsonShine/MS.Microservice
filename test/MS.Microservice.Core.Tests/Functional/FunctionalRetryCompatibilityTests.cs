using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Tests.Functional;

public class FunctionalRetryCompatibilityTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    public async Task NonPositiveRetries_InvokesOnceAndPreservesFailure(int retries, bool throwSynchronously)
    {
        var failure = new InvalidOperationException("only attempt");
        var attempts = 0;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => F.Retry(retries, 0, () =>
        {
            attempts++;
            if (throwSynchronously)
                throw failure;
            return Task.FromException<int>(failure);
        }));

        Assert.Equal(1, attempts);
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task FaultedTasks_UseExtraRetryCountAndPropagateLastFailure()
    {
        var failures = new[]
        {
            new InvalidOperationException("first"),
            new InvalidOperationException("second"),
            new InvalidOperationException("last")
        };
        var attempts = 0;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => F.Retry(2, 0,
            () => Task.FromException<int>(failures[attempts++])));

        Assert.Equal(3, attempts);
        Assert.Same(failures[2], actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentCancellation_RemainsRetriable(bool throwSynchronously)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempts = 0;

        var result = await F.Retry(1, 0, () =>
        {
            if (++attempts != 1)
                return Task.FromResult(42);
            if (throwSynchronously)
                throw new OperationCanceledException(cancellation.Token);
            return Task.FromCanceled<int>(cancellation.Token);
        });

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Exhaustion_PreservesOriginalOperationStack()
    {
        var failure = new InvalidOperationException("operation failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => F.Retry<int>(1, 0, () =>
        {
            ThrowFromOperation(failure);
            return Task.FromResult(42);
        }));

        Assert.Same(failure, actual);
        Assert.Contains(nameof(ThrowFromOperation), actual.StackTrace);
    }

    [Fact]
    public async Task NullResult_IsSuccessfulWithoutRetry()
    {
        var attempts = 0;
        var result = await F.Retry(2, 0, () =>
        {
            attempts++;
            return Task.FromResult<string?>(null);
        });

        Assert.Null(result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ZeroDelay_SupportsRepeatedImmediateFailures()
    {
        const int retries = 128;
        var attempts = 0;

        var result = await F.Retry(retries, 0, () => ++attempts <= retries
            ? Task.FromException<int>(new InvalidOperationException("retry"))
            : Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(retries + 1, attempts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromOperation(Exception failure) => throw failure;
}
