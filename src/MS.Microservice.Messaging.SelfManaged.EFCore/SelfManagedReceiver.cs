using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class SelfManagedReceiver<TContext>(IServiceScopeFactory scopeFactory, MessageTopology topology,
    SelfManagedOptions options, TimeProvider clock, MessagingDiagnostics diagnostics, ILogger<SelfManagedReceiver<TContext>> logger) : IMessageReceiver
    where TContext : DbContext
{

    public async Task<DeliveryResult> ReceiveAsync(SerializedMessage message, string consumer,
        CancellationToken cancellationToken)
    {
        var subscription = topology.Subscription(consumer);

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<InboxStore<TContext>>();
        var token = Guid.NewGuid();
        var started = clock.GetTimestamp();
        InboxClaim claim;
        while (true)
        {
            claim = await store.AcquireAsync(message, consumer, token, cancellationToken);
            if (claim.Result != InboxAcquisition.Busy) break;
            if (clock.GetElapsedTime(started) >= options.BusyWaitLimit) return Finish("busy", DeliveryResult.Requeue);
            await Task.Delay(options.BusyRecheckInterval, clock, cancellationToken);
        }
        if (claim.Result == InboxAcquisition.AlreadyProcessed) return Finish("duplicate", DeliveryResult.Acknowledge);
        if (claim.Result == InboxAcquisition.DeadLettered) return Finish("dead_letter", DeliveryResult.Reject);

        using var activity = diagnostics.Activities.StartActivity("messaging.consume", ActivityKind.Consumer,
            ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parent) ? parent : default);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = message.Id, ["MessageType"] = message.ContractName, ["Consumer"] = consumer, ["CorrelationId"] = message.CorrelationId
        });
        await using var guard = new LeaseGuard(async ct =>
        {
            await using var renewalScope = scopeFactory.CreateAsyncScope();
            return await renewalScope.ServiceProvider.GetRequiredService<InboxStore<TContext>>()
                .RenewAsync(message.Id, consumer, token, ct);
        }, options.ProcessingLease, options.ProcessingTimeout, clock, cancellationToken);
        try
        {
            var contract = topology.Registry.Get(subscription.MessageType);
            if (contract.Name != message.ContractName || contract.Version != message.ContractVersion)
                throw new MessageContractException("Message contract does not match its subscription.");
            var payload = topology.Registry.Deserialize(message);
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unit.ExecuteAsync(async ct =>
            {
                await subscription.DispatchAsync(scope.ServiceProvider, payload,
                    new(message.Id, consumer, message.CorrelationId, message.TraceParent, message.TraceState), ct);
                await guard.StopRenewingAsync();
                ct.ThrowIfCancellationRequested();
                if (!await store.CompleteAsync(message.Id, consumer, token, ct))
                    throw new OperationCanceledException("Inbox ownership was lost.", ct);
            }, guard.Token);
            logger.LogInformation("Message consumed");
            return Finish("consumed", DeliveryResult.Acknowledge);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || guard.Lost)
        {
            return Finish(guard.Lost ? "lease_lost" : "cancelled", DeliveryResult.Requeue);
        }
        catch (Exception exception)
        {
            await guard.StopRenewingAsync();
            var permanent = exception is MessageContractException or PermanentMessageException;
            logger.LogWarning("Consumption failed: {ErrorType}", exception.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var changed = await store.FailAsync(claim.Entry, token, exception.GetType().Name, permanent, failureTimeout.Token);
            var result = changed && (permanent || claim.Entry.AttemptCount + 1 > options.MaxRetryAttempts)
                ? DeliveryResult.Reject : DeliveryResult.Requeue;
            return Finish(result == DeliveryResult.Reject ? "dead_letter" : "consume_failed", result);
        }

        DeliveryResult Finish(string outcome, DeliveryResult result)
        {
            diagnostics.Record("SelfManaged", outcome, clock.GetElapsedTime(started).TotalMilliseconds, consumer);
            return result;
        }
    }
}
