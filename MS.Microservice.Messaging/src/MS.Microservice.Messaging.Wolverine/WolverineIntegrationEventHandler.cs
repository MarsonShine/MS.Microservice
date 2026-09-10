using Microsoft.EntityFrameworkCore;
using global::Wolverine;
using global::Wolverine.Attributes;

namespace MS.Microservice.Messaging.Wolverine;

public static class WolverineIntegrationEventHandler<TEvent, TContext>
    where TEvent : IIntegrationEvent where TContext : DbContext
{
    [Transactional]
    public static async Task Handle(TEvent message, TContext database, Envelope envelope, IMessageContext messages,
        MessageTopology topology, WolverineMessagingOptions options, WolverineUnitOfWork<TContext> unit,
        IServiceProvider services, CancellationToken cancellationToken)
    {
        if (message.Id != envelope.Id) throw new MessageContractException("Event identity does not match the native envelope.");
        topology.Registry.Serialize(message);
        var destination = Uri.UnescapeDataString(envelope.Destination?.Segments.LastOrDefault()?.Trim('/') ?? "");
        var subscription = topology.Subscriptions.SingleOrDefault(x => x.MessageType == typeof(TEvent)
            && (x.Consumer == envelope.EndpointName || options.BrokerOptions().Queue(x.Consumer) == destination))
            ?? throw new MessageContractException("Incoming endpoint has no registered subscription.");
        unit.BeginIncoming(messages);
        try
        {
            await subscription.DispatchAsync(services, message,
                new(message.Id, subscription.Consumer, envelope.CorrelationId,
                    envelope.Headers.GetValueOrDefault("traceparent"), envelope.Headers.GetValueOrDefault("tracestate")),
                cancellationToken);
            await unit.FlushIncomingAsync(cancellationToken);
        }
        finally { unit.EndIncoming(); }
    }
}
