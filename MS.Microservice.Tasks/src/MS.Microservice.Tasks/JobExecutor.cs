using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Tasks;

/// <summary>Executes persisted jobs in one host process with scoped handlers and bounded resource queues.</summary>
public sealed class JobExecutor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobExecutor> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _storageTimeout;
    private readonly Dictionary<(string Name, int Version), JobHandlerRegistration> _handlers;
    private readonly Dictionary<string, SemaphoreSlim> _queues;
    private readonly Dictionary<Guid, Task> _active = [];
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private bool _started;

    internal JobExecutor(IServiceScopeFactory scopes, TimeProvider clock, JobExecutionOptions options,
        IEnumerable<JobHandlerRegistration> handlers, ILogger<JobExecutor> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _pollInterval = options.CancellationPollInterval;
        _storageTimeout = options.StorageOperationTimeout;
        _handlers = handlers.ToDictionary(handler => (handler.Name, handler.Version));
        _queues = options.Queues.ToDictionary(queue => queue.Key,
            queue => new SemaphoreSlim(queue.Value, queue.Value), StringComparer.Ordinal);
        if (_handlers.Values.Any(handler => !_queues.ContainsKey(handler.Queue)))
            throw new InvalidOperationException("Every registered handler must use a configured execution queue.");
    }

    /// <summary>Recovers abandoned executions before dispatch; only explicitly safe handlers may restart.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _tickGate.WaitAsync(cancellationToken);
        try
        {
            if (_stopping.IsCancellationRequested) throw new InvalidOperationException("A stopped job executor cannot restart.");
            if (_started) return;
            while (true)
            {
                var running = await WithStoreAsync((store, token) => store.GetRunningExecutionsAsync(100, token), cancellationToken);
                if (running.Count == 0) break;
                foreach (var execution in running)
                {
                    var safe = _handlers.TryGetValue((execution.Job.HandlerName, execution.Job.HandlerVersion), out var handler)
                        && handler.SafeToRetry && handler.Queue == execution.Job.Queue;
                    await WithStoreAsync((store, token) => store.RecoverAsync(execution.Attempt, safe, token), cancellationToken);
                }
            }
            _started = true;
        }
        finally { _tickGate.Release(); }
    }

    /// <summary>Claims available work without waiting for handlers to finish; repeated ticks cannot exceed queue capacity.</summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        await _tickGate.WaitAsync(cancellationToken);
        try
        {
            if (!_started || _stopping.IsCancellationRequested) return;
            using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            foreach (var queue in _queues)
            {
                var available = queue.Value.CurrentCount;
                if (available == 0) continue;
                var candidates = await WithStoreAsync((store, token) => store.GetDueAsync(queue.Key, available, token), dispatch.Token);
                foreach (var candidate in candidates)
                {
                    if (_stopping.IsCancellationRequested || !queue.Value.Wait(0)) break;
                    var dispatched = false;
                    try
                    {
                        var execution = await WithStoreAsync((store, token) => store.TryStartAsync(candidate.Scope, candidate.JobId, token), dispatch.Token);
                        if (execution is null) continue;
                        _handlers.TryGetValue((execution.Job.HandlerName, execution.Job.HandlerVersion), out var handler);
                        lock (_active)
                            _active.Add(execution.Attempt.Token, Task.Run(() => ExecuteAsync(execution, handler, queue.Value)));
                        dispatched = true;
                    }
                    finally { if (!dispatched) queue.Value.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        finally { _tickGate.Release(); }
    }

    /// <summary>Stops new claims and cooperatively cancels handlers; capacity remains held until handlers and scopes finish.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try { await _stopping.CancelAsync().WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { _logger.LogWarning(error, "A task cancellation callback failed during shutdown."); }
        await _tickGate.WaitAsync(cancellationToken);
        Task[] pending;
        try { lock (_active) pending = _active.Values.ToArray(); }
        finally { _tickGate.Release(); }
        await Task.WhenAll(pending).WaitAsync(cancellationToken);
    }

    private async Task ExecuteAsync(JobExecution execution, JobHandlerRegistration? handler, SemaphoreSlim capacity)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = execution.Job.JobId, ["JobScope"] = execution.Job.Scope,
            ["Handler"] = execution.Job.HandlerName, ["Attempt"] = execution.Job.AttemptCount
        });
        try
        {
            if (handler is null || handler.Queue != execution.Job.Queue)
            {
                await WithStoreAsync((store, token) => store.FailAsync(execution.Attempt,
                    handler is null ? "handler_not_registered" : "queue_not_allowed", token));
                return;
            }
            using var timeout = new CancellationTokenSource(handler.Timeout, _clock);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, timeout.Token);
            using var observerStop = new CancellationTokenSource();
            var observationFailed = false;
            var observer = ObserveCancellationAsync(execution, work, observerStop.Token, () => observationFailed = true);
            string? result = null;
            Exception? failure = null;
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var context = new JobExecutionContext(execution.Job, (progress, message) =>
                    WithStoreAsync((store, token) => store.ReportProgressAsync(execution.Attempt, progress, message, token), work.Token));
                work.Token.ThrowIfCancellationRequested();
                result = await handler.ExecuteAsync(scope.ServiceProvider, execution.Job.PayloadJson, context, work.Token);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                await observerStop.CancelAsync();
                await observer;
            }

            // Publish a terminal state only after the handler and its DI scope have actually ended.
            if (_stopping.IsCancellationRequested)
                await WithStoreAsync((store, token) => store.RecoverAsync(execution.Attempt, handler.SafeToRetry, token));
            else if (timeout.IsCancellationRequested || observationFailed || failure is not null)
            {
                var code = timeout.IsCancellationRequested ? "execution_timeout"
                    : observationFailed ? "cancellation_observation_failed"
                    : failure is JsonException ? "payload_invalid" : "handler_failed";
                if (failure is not null && !work.IsCancellationRequested)
                    _logger.LogWarning(failure, "Task handler failed with {ErrorCode}.", code);
                await WithStoreAsync((store, token) => store.FailAsync(execution.Attempt, code, token));
            }
            else if (result is not null && (string.IsNullOrWhiteSpace(result) || result.Length > 4096))
                await WithStoreAsync((store, token) => store.FailAsync(execution.Attempt, "result_reference_invalid", token));
            else await WithStoreAsync((store, token) => store.CompleteAsync(execution.Attempt, result, token));
        }
        catch (Exception error)
        {
            // A failed final write leaves Running durable for the next startup recovery; do not fabricate success.
            _logger.LogError(error, "Task execution could not publish its final state.");
        }
        finally
        {
            capacity.Release();
            lock (_active) _active.Remove(execution.Attempt.Token);
        }
    }

    private async Task ObserveCancellationAsync(JobExecution execution, CancellationTokenSource work,
        CancellationToken stoppingToken, Action failed)
    {
        try
        {
            while (true)
            {
                var state = await WithStoreAsync((store, token) => store.GetExecutionStatusAsync(execution.Attempt, token), stoppingToken);
                if (state != JobExecutionStatus.Running)
                {
                    await work.CancelAsync();
                    return;
                }
                await Task.Delay(_pollInterval, _clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            failed();
            _logger.LogWarning(error, "Task cancellation observation failed; cancelling the handler.");
            try { await work.CancelAsync(); }
            catch (Exception callbackError) { _logger.LogWarning(callbackError, "A task cancellation callback failed."); }
        }
    }

    private async Task<T> WithStoreAsync<T>(Func<IJobStore, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(_storageTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        await using var scope = _scopes.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<IJobStore>(), linked.Token);
    }
}
