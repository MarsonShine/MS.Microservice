using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

/// <summary>Applies admission policies and records provider outcomes without treating reporting as provider work.</summary>
public sealed class AIProductionPipeline(
    IAIRateLimiter rateLimiter,
    IAICircuitBreaker circuitBreaker,
    IAICostReporter costReporter,
    IOptionsMonitor<AICostAccountingOptions> costOptions,
    TimeProvider timeProvider,
    ILogger<AIProductionPipeline>? logger = null)
{
    private readonly ILogger<AIProductionPipeline> log = logger ?? NullLogger<AIProductionPipeline>.Instance;

    public async ValueTask<TResponse> ExecuteAsync<TResponse>(AIRequestContext context,
        Func<CancellationToken, ValueTask<TResponse>> operation, Func<TResponse, AIUsage> getUsage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(getUsage);
        cancellationToken.ThrowIfCancellationRequested();
        await circuitBreaker.EnsureAllowedAsync(context, cancellationToken).ConfigureAwait(false);
        await using var lease = await rateLimiter.AcquireAsync(context, cancellationToken).ConfigureAwait(false);
        var startedAt = timeProvider.GetTimestamp();
        TResponse response;
        AIUsage usage;
        try
        {
            response = await operation(cancellationToken).ConfigureAwait(false);
            usage = getUsage(response);
        }
        catch (Exception exception)
        {
            await ObserveAsync(context, AIUsage.Zero, startedAt, false, exception,
                exception is OperationCanceledException && cancellationToken.IsCancellationRequested, cancellationToken).ConfigureAwait(false);
            throw;
        }
        await ObserveAsync(context, usage, startedAt, true, null, false, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async IAsyncEnumerable<TChunk> ExecuteStreamAsync<TChunk>(AIRequestContext context,
        Func<CancellationToken, IAsyncEnumerable<TChunk>> operation, Func<TChunk, AIUsage?> getUsage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(getUsage);
        cancellationToken.ThrowIfCancellationRequested();
        await circuitBreaker.EnsureAllowedAsync(context, cancellationToken).ConfigureAwait(false);
        await using var lease = await rateLimiter.AcquireAsync(context, cancellationToken).ConfigureAwait(false);
        var startedAt = timeProvider.GetTimestamp();
        var usage = AIUsage.Zero;
        var completed = false;
        Exception? failure = null;
        IAsyncEnumerator<TChunk>? enumerator = null;
        try
        {
            try { enumerator = operation(cancellationToken).GetAsyncEnumerator(cancellationToken); }
            catch (Exception exception) { failure = exception; throw; }
            while (true)
            {
                TChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    chunk = enumerator.Current;
                    usage = getUsage(chunk) ?? usage;
                }
                catch (Exception exception) { failure = exception; throw; }
                yield return chunk;
            }
            completed = true;
        }
        finally
        {
            Exception? disposalFailure = null;
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    if (failure is null) failure = disposalFailure = exception;
                    else LogPolicyFailure("stream_disposal", exception);
                }
            }
            var abandoned = !completed && failure is null;
            if (abandoned) failure = new OperationCanceledException("The consumer ended the stream.");
            await ObserveAsync(context, usage, startedAt, completed && failure is null, failure,
                abandoned || failure is OperationCanceledException && cancellationToken.IsCancellationRequested,
                cancellationToken).ConfigureAwait(false);
            if (disposalFailure is not null) ExceptionDispatchInfo.Capture(disposalFailure).Throw();
        }
    }

    private async ValueTask ObserveAsync(AIRequestContext context, AIUsage usage, long startedAt,
        bool succeeded, Exception? exception, bool interrupted, CancellationToken cancellationToken)
    {
        try
        {
            if (succeeded) await circuitBreaker.RecordSuccessAsync(context, cancellationToken).ConfigureAwait(false);
            else if (!interrupted) await circuitBreaker.RecordFailureAsync(context, exception!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception policyFailure) { LogPolicyFailure("circuit_recording", policyFailure); }

        if (!costOptions.CurrentValue.Enabled) return;
        try
        {
            await costReporter.ReportAsync(new AICostRecord
            {
                Provider = context.Provider, Model = context.Model, Capability = context.Capability,
                Scenario = context.Scenario, RequestId = context.RequestId,
                InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens, TotalTokens = usage.TotalTokens,
                Duration = timeProvider.GetElapsedTime(startedAt), Succeeded = succeeded,
                ExceptionCategory = exception is AIException aiException ? aiException.ErrorCode : exception?.GetType().Name
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception reportingFailure) { LogPolicyFailure("cost_reporting", reportingFailure); }
    }

    private void LogPolicyFailure(string policy, Exception exception)
        => log.LogWarning("AI {Policy} failed with {FailureType}", policy, exception.GetType().Name);
}
