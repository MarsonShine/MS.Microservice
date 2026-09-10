using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class SelfManagedPublisher<TContext>(OutboxStore<TContext> store, IMessageTransport transport,
    MessageContractRegistry contracts, IServiceScopeFactory scopeFactory, SelfManagedOptions options,
    TimeProvider clock, MessagingDiagnostics diagnostics, ILogger<SelfManagedPublisher<TContext>> logger)
    where TContext : DbContext
{
    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        var messages = await store.ClaimAsync(token, cancellationToken);
        var published = 0;
        foreach (var entry in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await store.RenewAsync(entry.Id, token, cancellationToken)) continue;
            var started = Stopwatch.GetTimestamp();
            var message = entry.ToMessage();
            using var activity = diagnostics.Activities.StartActivity("messaging.publish", ActivityKind.Producer,
                ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parent) ? parent : default);
            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["MessageId"] = message.Id, ["MessageType"] = message.ContractName, ["CorrelationId"] = message.CorrelationId
            });
            await using var guard = new LeaseGuard(async ct =>
            {
                await using var renewalScope = scopeFactory.CreateAsyncScope();
                return await renewalScope.ServiceProvider.GetRequiredService<OutboxStore<TContext>>()
                    .RenewAsync(message.Id, token, ct);
            }, options.PublishingLease, options.ProcessingTimeout, clock, cancellationToken);
            try
            {
                contracts.Deserialize(message);
                await transport.SendConfirmedAsync(message with
                {
                    TraceParent = activity?.Id ?? message.TraceParent,
                    TraceState = activity?.TraceStateString ?? message.TraceState
                }, guard.Token);
                await guard.StopRenewingAsync();
                guard.Token.ThrowIfCancellationRequested();
                if (await store.CompleteAsync(entry.Id, token, cancellationToken))
                {
                    published++;
                    diagnostics.Record("SelfManaged", "published", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    logger.LogInformation("Message publication confirmed");
                }
                else diagnostics.Record("SelfManaged", "lease_lost", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await guard.StopRenewingAsync();
                var permanent = exception is MessageContractException or PermanentMessageException;
                var updated = await store.FailAsync(entry, token, exception.GetType().Name, permanent, cancellationToken);
                diagnostics.Record("SelfManaged", updated ? "publish_failed" : "lease_lost",
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                logger.LogWarning("Publication deferred: {ErrorType}", exception.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            }
        }
        return published;
    }
}
