using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Messaging;
using MS.Microservice.Persistence.EFCore.Inbox;
using MS.Microservice.Infrastructure.Telemetry;
using Wolverine;

namespace MS.Microservice.Infrastructure.Messaging;

public sealed record InboxExecution(
    string DeduplicationKey,
    Guid ProcessingToken,
    bool ShouldExecute,
    IInboxTransaction? BusinessTransaction,
    long StartedTimestamp)
{
    public bool Completed { get; set; }
}

public sealed class InboxConsumptionMiddleware
{
    public static async Task<(HandlerContinuation, InboxExecution)> BeforeAsync<TMessage>(
        TMessage message,
        Envelope envelope,
        IInboxStore inboxStore,
        IInboxTransactionCoordinator transactionCoordinator,
        PlatformMetrics metrics,
        IOptions<InboxConsumerOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
        where TMessage : IEventContract
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(envelope);

        var messageId = ResolveMessageId(message, envelope);
        if (messageId == Guid.Empty)
        {
            throw new InvalidOperationException("Incoming event has no stable message identifier.");
        }

        var consumer = BuildConsumerName<TMessage>(envelope);
        var nowUtc = timeProvider.GetUtcNow();
        var registration = await inboxStore.TryRegisterAsync(
            messageId,
            consumer,
            nowUtc,
            envelope.MessageType ?? typeof(TMessage).AssemblyQualifiedName,
            envelope.Source?.ToString(),
            Activity.Current?.TraceId.ToString(),
            envelope.CorrelationId,
            cancellationToken);
        metrics.RecordInboxRegistration(registration.IsFirstDelivery);
        var processingToken = Guid.NewGuid();
        var acquired = await inboxStore.TryBeginProcessingAsync(
            registration.Receipt.DeduplicationKey,
            processingToken,
            nowUtc,
            options.Value.ProcessingLease,
            cancellationToken);
        var businessTransaction = acquired
            ? await transactionCoordinator.BeginAsync(cancellationToken)
            : null;
        var execution = new InboxExecution(
            registration.Receipt.DeduplicationKey,
            processingToken,
            acquired,
            businessTransaction,
            Stopwatch.GetTimestamp());
        if (!acquired)
        {
            metrics.RecordInboxShortCircuited();
        }
        return (acquired ? HandlerContinuation.Continue : HandlerContinuation.Stop, execution);
    }

    private static Guid ResolveMessageId<TMessage>(TMessage message, Envelope envelope)
        where TMessage : IEventContract
    {
        if (envelope.TryGetHeader(MessageHeaders.MessageId, out var stableMessageId)
            && Guid.TryParse(stableMessageId, out var parsedMessageId))
        {
            return parsedMessageId;
        }

        if (envelope.Id != Guid.Empty)
        {
            return envelope.Id;
        }

        return message is IntegrationEvent integrationEvent
            ? integrationEvent.Id
            : Guid.Empty;
    }

    public static async Task AfterAsync(
        InboxExecution execution,
        IInboxStore inboxStore,
        IInboxTransactionCoordinator transactionCoordinator,
        PlatformMetrics metrics,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!execution.ShouldExecute)
        {
            return;
        }

        await transactionCoordinator.SaveChangesAsync(cancellationToken);
        var markedProcessed = await inboxStore.MarkProcessedAsync(
            execution.DeduplicationKey,
            execution.ProcessingToken,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!markedProcessed)
        {
            throw new InvalidOperationException("Inbox processing lease was lost before completion.");
        }

        await execution.BusinessTransaction!.CommitAsync(cancellationToken);
        execution.Completed = true;
        metrics.RecordInboxProcessed(
            Stopwatch.GetElapsedTime(execution.StartedTimestamp).TotalMilliseconds);
    }

    public static async Task FinallyAsync(
        InboxExecution execution,
        IInboxStore inboxStore,
        PlatformMetrics metrics,
        ILogger<InboxExecution> logger,
        CancellationToken cancellationToken)
    {
        if (!execution.ShouldExecute || execution.BusinessTransaction is null)
        {
            return;
        }

        try
        {
            if (!execution.Completed)
            {
                await execution.BusinessTransaction.RollbackAsync(CancellationToken.None);
            }
        }
        finally
        {
            await execution.BusinessTransaction.DisposeAsync();
        }

        if (execution.Completed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await inboxStore.MarkFailedAsync(
                execution.DeduplicationKey,
                execution.ProcessingToken,
                "Handler execution did not complete successfully.",
                CancellationToken.None);
            metrics.RecordInboxFailed(
                Stopwatch.GetElapsedTime(execution.StartedTimestamp).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unable to record failed Inbox execution for {DeduplicationKey}",
                execution.DeduplicationKey);
        }
    }

    private static string BuildConsumerName<TMessage>(Envelope envelope)
    {
        var endpoint = !string.IsNullOrWhiteSpace(envelope.EndpointName)
            ? envelope.EndpointName
            : envelope.Destination?.ToString() ?? "local";
        return $"{endpoint}:{typeof(TMessage).FullName}";
    }
}
