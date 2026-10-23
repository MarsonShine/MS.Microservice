namespace MS.Microservice.Threading;

/// <summary>Scope-bound ownership of one permit from a caller-owned semaphore.</summary>
public static class SemaphoreSlimExtensions
{
    /// <summary>Waits for one permit; disposing the returned lease releases it exactly once.</summary>
    /// <remarks>The caller must keep the semaphore alive until all waiters and leases have finished.</remarks>
    public static async ValueTask<IDisposable> AcquireAsync(this SemaphoreSlim semaphore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semaphore);
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
