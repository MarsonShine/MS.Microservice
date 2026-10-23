using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MS.Microservice.Core.Common.Advance.Resilience;

/// <summary>Executes retry strategies and conditions while preserving the legacy entry points.</summary>
public class RetryExecutor(IRetryStrategy strategy, IRetryCondition condition)
{
    private readonly IRetryStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly IRetryCondition _condition = condition ?? throw new ArgumentNullException(nameof(condition));
    private ILogger _logger = NullLogger<RetryExecutor>.Instance;

    public ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger<RetryExecutor>.Instance;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await ExecuteCancellableAsync(_ => operation(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Passes caller cancellation to each operation and to retry waits.</summary>
    /// <remarks>Operations must cooperate with cancellation; no in-flight operation is forcibly abandoned.</remarks>
    public async Task<T> ExecuteCancellableAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var context = new RetryContext();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Retry attempt {Attempt} starting", context.Attempt);
            Exception lastException;
            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!_condition.ShouldRetry(result, null))
                {
                    _logger.LogDebug("Operation succeeded on attempt {Attempt}", context.Attempt);
                    return result;
                }
                lastException = new InvalidOperationException("Result does not meet retry condition");
                _logger.LogWarning(lastException, "Result does not meet retry condition on attempt {Attempt}", context.Attempt);
            }
            catch (Exception error)
            {
                // Caller cancellation wins over retry predicates, including broad Exception predicates.
                cancellationToken.ThrowIfCancellationRequested();
                lastException = error;
                if (!_condition.ShouldRetry<object?>(null, error))
                {
                    _logger.LogError(error, "Operation failed with non-retriable exception on attempt {Attempt}", context.Attempt);
                    throw;
                }
                _logger.LogWarning(error, "Operation failed with retriable exception on attempt {Attempt}", context.Attempt);
            }

            context.LastException = lastException;
            cancellationToken.ThrowIfCancellationRequested();
            if (!_strategy.ShouldRetry(context))
            {
                _logger.LogError(lastException, "Retry attempts exhausted after {Attempts} attempts", context.Attempt);
                ExceptionDispatchInfo.Capture(lastException).Throw();
            }
            var delay = _strategy.GetDelay(context);
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Waiting {Delay} before next retry attempt {NextAttempt}", delay, context.Attempt + 1);
                await WaitForRetryAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            context.Attempt++;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteCancellableAsync(_ => operation(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteCancellableAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteCancellableAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitForRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        // Preserve long policy delays without exceeding the runtime's per-timer limit.
        var maximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
        while (delay > maximumTimerDelay)
        {
            await Task.Delay(maximumTimerDelay, cancellationToken).ConfigureAwait(false);
            delay -= maximumTimerDelay;
        }
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    public T Execute<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(() => Task.FromResult(operation())).GetAwaiter().GetResult();
    }

    public void Execute(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Execute(() => { operation(); return true; });
    }
}
