using Xunit;

namespace MS.Microservice.Threading.Tests;

public class SemaphoreLeaseTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LeaseHonorsSemaphoreCapacity(int capacity)
    {
        using var semaphore = new SemaphoreSlim(capacity, capacity);
        var leases = new List<IDisposable>();
        try
        {
            for (var index = 0; index < capacity; index++)
                leases.Add(await semaphore.AcquireAsync().AsTask().WaitAsync(TestTimeout));

            Assert.Equal(0, semaphore.CurrentCount);
            var waiting = semaphore.AcquireAsync().AsTask();
            Assert.False(waiting.IsCompleted);

            leases[0].Dispose();
            leases.Add(await waiting.WaitAsync(TestTimeout));
            Assert.Equal(0, semaphore.CurrentCount);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }

        Assert.Equal(capacity, semaphore.CurrentCount);
    }

    [Fact]
    public async Task CancelingWaitDoesNotReleaseAnUnacquiredPermit()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        using var stop = new CancellationTokenSource();
        var owner = await semaphore.AcquireAsync().AsTask().WaitAsync(TestTimeout);
        try
        {
            var waiting = semaphore.AcquireAsync(stop.Token).AsTask();
            Assert.False(waiting.IsCompleted);
            stop.Cancel();

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TestTimeout));
            Assert.Equal(stop.Token, error.CancellationToken);
            Assert.Equal(0, semaphore.CurrentCount);
        }
        finally { owner.Dispose(); }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task AlreadyCanceledWaitDoesNotConsumeAnAvailablePermit()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await semaphore.AcquireAsync(stop.Token).AsTask().WaitAsync(TestTimeout));

        Assert.Equal(stop.Token, error.CancellationToken);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task ScopeFailureReturnsPermitWithoutDisposingSemaphore()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var expected = new IOException("operation failed");

        async Task OperationAsync()
        {
            using var lease = await semaphore.AcquireAsync().AsTask().WaitAsync(TestTimeout);
            Assert.Equal(0, semaphore.CurrentCount);
            throw expected;
        }

        Assert.Same(expected, await Assert.ThrowsAsync<IOException>(OperationAsync));
        Assert.Equal(1, semaphore.CurrentCount);

        // The lease borrows the semaphore; its owner can keep using the original API.
        Assert.True(semaphore.Wait(0));
        Assert.Equal(0, semaphore.CurrentCount);
        semaphore.Release();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedDisposeReturnsExactlyOnePermit()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var lease = await semaphore.AcquireAsync().AsTask().WaitAsync(TestTimeout);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            lease.Dispose();
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(disposers).WaitAsync(TestTimeout);
        lease.Dispose();
        Assert.Equal(1, semaphore.CurrentCount);

        using (await semaphore.AcquireAsync().AsTask().WaitAsync(TestTimeout))
        {
            Assert.Equal(0, semaphore.CurrentCount);
            // Releasing the old lease again must not affect a later owner.
            lease.Dispose();
            Assert.Equal(0, semaphore.CurrentCount);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }
}
