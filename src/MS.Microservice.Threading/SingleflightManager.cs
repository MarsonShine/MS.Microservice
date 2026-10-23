using System.Collections.Concurrent;

namespace MS.Microservice.Threading;

/// <summary>Shares one in-flight operation per key and result type within this instance.</summary>
public sealed class SingleflightManager
{
    private readonly ConcurrentDictionary<(string Key, Type ResultType), TaskCompletionSource<object?>> _inFlight = new();

    public Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action) =>
        ExecuteOnceAsync(key, action, CancellationToken.None);

    /// <summary>Cancellation stops only this caller's wait, including the caller that starts the operation.</summary>
    /// <remarks>The factory controls operation cancellation. Completed values are not cached.</remarks>
    public async Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = (key, typeof(T));
        var candidate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _inFlight.GetOrAdd(identity, candidate);
        if (ReferenceEquals(shared, candidate))
            _ = PublishAsync(identity, shared, action);
        return (T)(await shared.Task.WaitAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async Task PublishAsync<T>((string Key, Type ResultType) identity,
        TaskCompletionSource<object?> completion, Func<Task<T>> action)
    {
        try
        {
            T result;
            try { result = await action().ConfigureAwait(false); }
            finally
            {
                // Remove only this flight, before waking callers that may start the next flight.
                _inFlight.TryRemove(new KeyValuePair<(string, Type), TaskCompletionSource<object?>>(identity, completion));
            }
            completion.SetResult(result);
        }
        catch (OperationCanceledException error) { completion.SetCanceled(error.CancellationToken); }
        catch (Exception error)
        {
            completion.SetException(error);
            // A fault must be observed even if every caller has canceled its own wait.
            _ = completion.Task.Exception;
        }
    }
}
