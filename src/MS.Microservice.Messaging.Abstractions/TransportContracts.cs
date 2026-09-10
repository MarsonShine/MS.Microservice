namespace MS.Microservice.Messaging;

/// <summary>Immutable serialized event; identity and type metadata are independent of CLR assembly names.</summary>
public sealed record SerializedMessage(Guid Id, string ContractName, int ContractVersion,
    DateTimeOffset OccurredAtUtc, string Payload, string? CorrelationId = null,
    string? TraceParent = null, string? TraceState = null);

/// <summary>Transport extension point. Success means positive broker confirmation, including routing.</summary>
public interface IMessageTransport
{
    Task SendConfirmedAsync(SerializedMessage message, CancellationToken cancellationToken);
}

public enum DeliveryResult { Acknowledge, Requeue, Reject }

/// <summary>Receives one delivery. Only Acknowledge permits a successful broker acknowledgment.</summary>
public interface IMessageReceiver
{
    Task<DeliveryResult> ReceiveAsync(SerializedMessage message, string consumer,
        CancellationToken cancellationToken);
}

public sealed class MessageSubscription
{
    private readonly Func<IServiceProvider, IIntegrationEvent, MessageContext, CancellationToken, Task> _dispatch;
    public Type MessageType { get; }
    public Type HandlerType { get; }
    public string Consumer { get; }

    private MessageSubscription(Type messageType, Type handlerType, string consumer,
        Func<IServiceProvider, IIntegrationEvent, MessageContext, CancellationToken, Task> dispatch)
        => (MessageType, HandlerType, Consumer, _dispatch) = (messageType, handlerType, consumer, dispatch);

    public static MessageSubscription For<TEvent, THandler>(string consumer)
        where TEvent : IIntegrationEvent where THandler : class, IIntegrationEventHandler<TEvent>
        => new(typeof(TEvent), typeof(THandler), consumer, (services, message, context, token) =>
            ((THandler?)services.GetService(typeof(THandler))
                ?? throw new InvalidOperationException($"Handler {typeof(THandler).Name} is not registered."))
            .HandleAsync((TEvent)message, context, token));

    public Task DispatchAsync(IServiceProvider services, IIntegrationEvent message, MessageContext context,
        CancellationToken cancellationToken) => _dispatch(services, message, context, cancellationToken);
}
