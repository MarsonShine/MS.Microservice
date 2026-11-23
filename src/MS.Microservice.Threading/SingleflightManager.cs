using System.Collections.Concurrent;

namespace MS.Microservice.Threading;

/// <summary>Shares one in-flight operation per key and result type within this instance.</summary>
public sealed class SingleflightManager
{
    private readonly ConcurrentDictionary<(string Key, Type ResultType), Task> _inFlight = new();

    public Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action) =>
        ExecuteOnceAsync(key, action, CancellationToken.None);

    /// <summary>Cancellation stops only this caller's wait, including the caller that starts the operation.</summary>
    /// <remarks>The factory controls operation cancellation. Completed values are not cached.</remarks>
    public Task<T> ExecuteOnceAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (key is not null && action is not null && !cancellationToken.IsCancellationRequested
            && _inFlight.TryGetValue((key, typeof(T)), out var shared))
        {
            return ((Task<T>)shared).WaitAsync(cancellationToken);
        }
        return StartOrJoinAsync(key, action, cancellationToken);
    }

    private async Task<T> StartOrJoinAsync<T>(string? key, Func<Task<T>>? action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = (key, typeof(T));
        var candidate = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _inFlight.GetOrAdd(identity, candidate.Task);
        if (ReferenceEquals(shared, candidate.Task))
        {
            _ = PublishAsync(identity, candidate, action);
        }
        return await ((Task<T>)shared).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync<T>((string Key, Type ResultType) identity,
        TaskCompletionSource<T> completion, Func<Task<T>> action)
    {
        try
        {
            T result;
            try
            {
                result = await action().ConfigureAwait(false);
            }
            finally
            {
                // Remove only this flight, before waking callers that may start the next flight.
                _inFlight.TryRemove(new KeyValuePair<(string, Type), Task>(identity, completion.Task));
            }
            completion.SetResult(result);
        }
        catch (OperationCanceledException error)
        {
            completion.SetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.SetException(error);
            // A fault must be observed even if every caller has canceled its own wait.
            _ = completion.Task.Exception;
        }
    }
}
