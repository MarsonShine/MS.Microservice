using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

/// <summary>Wraps AI provider calls with production policies such as rate limiting, circuit breaking, and cost reporting.</summary>
public sealed class AIProductionPipeline
{
    private readonly IAIRateLimiter _rateLimiter;
    private readonly IAICircuitBreaker _circuitBreaker;
    private readonly IAICostReporter _costReporter;
    private readonly IOptionsMonitor<AICostAccountingOptions> _costOptions;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of <see cref="AIProductionPipeline" />.</summary>
    public AIProductionPipeline(
        IAIRateLimiter rateLimiter,
        IAICircuitBreaker circuitBreaker,
        IAICostReporter costReporter,
        IOptionsMonitor<AICostAccountingOptions> costOptions,
        TimeProvider timeProvider)
    {
        _rateLimiter = rateLimiter;
        _circuitBreaker = circuitBreaker;
        _costReporter = costReporter;
        _costOptions = costOptions;
        _timeProvider = timeProvider;
    }

    /// <summary>Executes a non-streaming AI operation with production policies.</summary>
    public async ValueTask<TResponse> ExecuteAsync<TResponse>(
        AIRequestContext context,
        Func<CancellationToken, ValueTask<TResponse>> operation,
        Func<TResponse, AIUsage> getUsage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(getUsage);

        await _circuitBreaker.EnsureAllowedAsync(context, cancellationToken).ConfigureAwait(false);
        await using var lease = await _rateLimiter.AcquireAsync(context, cancellationToken).ConfigureAwait(false);
        var startedAt = _timeProvider.GetTimestamp();

        try
        {
            var response = await operation(cancellationToken).ConfigureAwait(false);
            await _circuitBreaker.RecordSuccessAsync(context, cancellationToken).ConfigureAwait(false);
            await ReportAsync(context, getUsage(response), _timeProvider.GetElapsedTime(startedAt), succeeded: true, exception: null, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception exception)
        {
            await _circuitBreaker.RecordFailureAsync(context, exception, cancellationToken).ConfigureAwait(false);
            await ReportAsync(context, AIUsage.Zero, _timeProvider.GetElapsedTime(startedAt), succeeded: false, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Executes a streaming AI operation with production policies.</summary>
    public async IAsyncEnumerable<TChunk> ExecuteStreamAsync<TChunk>(
        AIRequestContext context,
        Func<CancellationToken, IAsyncEnumerable<TChunk>> operation,
        Func<TChunk, AIUsage?> getUsage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(getUsage);

        await _circuitBreaker.EnsureAllowedAsync(context, cancellationToken).ConfigureAwait(false);
        var lease = await _rateLimiter.AcquireAsync(context, cancellationToken).ConfigureAwait(false);
        var enumerator = operation(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var startedAt = _timeProvider.GetTimestamp();
        var usage = AIUsage.Zero;
        var completed = false;
        Exception? failure = null;

        try
        {
            while (true)
            {
                TChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    chunk = enumerator.Current;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    throw;
                }

                usage = getUsage(chunk) ?? usage;
                yield return chunk;
            }

            completed = true;
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await lease.DisposeAsync().ConfigureAwait(false);
            var duration = _timeProvider.GetElapsedTime(startedAt);

            if (completed)
            {
                await _circuitBreaker.RecordSuccessAsync(context, cancellationToken).ConfigureAwait(false);
                await ReportAsync(context, usage, duration, succeeded: true, exception: null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                failure ??= new OperationCanceledException(cancellationToken);
                await _circuitBreaker.RecordFailureAsync(context, failure, cancellationToken).ConfigureAwait(false);
                await ReportAsync(context, usage, duration, succeeded: false, failure, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask ReportAsync(AIRequestContext context, AIUsage usage, TimeSpan duration, bool succeeded, Exception? exception, CancellationToken cancellationToken)
    {
        if (!_costOptions.CurrentValue.Enabled)
        {
            return;
        }

        await _costReporter.ReportAsync(new AICostRecord
        {
            Provider = context.Provider,
            Model = context.Model,
            Capability = context.Capability,
            Scenario = context.Scenario,
            RequestId = context.RequestId,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
            Duration = duration,
            Succeeded = succeeded,
            ExceptionCategory = exception is AIException aiException ? aiException.ErrorCode : exception?.GetType().Name,
        }, cancellationToken).ConfigureAwait(false);
    }
}
