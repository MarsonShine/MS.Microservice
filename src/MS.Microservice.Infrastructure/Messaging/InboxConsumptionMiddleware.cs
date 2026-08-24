using System.Diagnostics;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Messaging;
using MS.Microservice.Persistence.EFCore.Inbox;
using Wolverine;

namespace MS.Microservice.Infrastructure.Messaging;

public sealed record InboxExecution(string DeduplicationKey, Guid ProcessingToken, bool ShouldExecute)
{
    public bool Completed { get; set; }
}

public sealed class InboxConsumptionMiddleware
{
    public static async Task<(HandlerContinuation, InboxExecution)> BeforeAsync<TMessage>(
        TMessage message,
        Envelope envelope,
        IInboxStore inboxStore,
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
        var processingToken = Guid.NewGuid();
        var acquired = await inboxStore.TryBeginProcessingAsync(
            registration.Receipt.DeduplicationKey,
            processingToken,
            nowUtc,
            options.Value.ProcessingLease,
            cancellationToken);
        var execution = new InboxExecution(registration.Receipt.DeduplicationKey, processingToken, acquired);
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
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!execution.ShouldExecute)
        {
            return;
        }

        execution.Completed = await inboxStore.MarkProcessedAsync(
            execution.DeduplicationKey,
            execution.ProcessingToken,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public static async Task FinallyAsync(
        InboxExecution execution,
        IInboxStore inboxStore,
        CancellationToken cancellationToken)
    {
        if (!execution.ShouldExecute || execution.Completed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await inboxStore.MarkFailedAsync(
            execution.DeduplicationKey,
            execution.ProcessingToken,
            "Handler execution did not complete successfully.",
            cancellationToken);
    }

    private static string BuildConsumerName<TMessage>(Envelope envelope)
    {
        var endpoint = !string.IsNullOrWhiteSpace(envelope.EndpointName)
            ? envelope.EndpointName
            : envelope.Destination?.ToString() ?? "local";
        return $"{endpoint}:{typeof(TMessage).FullName}";
    }
}
