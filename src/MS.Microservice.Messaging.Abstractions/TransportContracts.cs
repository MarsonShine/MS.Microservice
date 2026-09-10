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

public sealed record MessageSubscription(Type MessageType, Type HandlerType, string Consumer);
