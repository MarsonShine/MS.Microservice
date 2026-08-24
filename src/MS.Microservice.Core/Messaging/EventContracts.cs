using System.Text.Json.Serialization;

namespace MS.Microservice.Core.Messaging;

/// <summary>Transport-independent header names used by platform messaging.</summary>
public static class MessageHeaders
{
    /// <summary>Stable message id preserved across outbox retries and transport redelivery.</summary>
    public const string MessageId = "ms-microservice-message-id";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string CorrelationId = "ms-microservice-correlation-id";
}

/// <summary>Canonical marker for events exchanged by platform messaging components.</summary>
public interface IEventContract
{
}

/// <summary>Canonical marker for in-process events raised by a domain model.</summary>
public interface IDomainEvent : IEventContract
{
}

/// <summary>Canonical marker for events published across an application boundary.</summary>
public interface IIntegrationEvent : IEventContract
{
}

/// <summary>Base integration-event contract with stable message identity and occurrence time.</summary>
public class IntegrationEvent : IIntegrationEvent
{
    /// <summary>Creates a new integration event.</summary>
    public IntegrationEvent()
        : this(Guid.NewGuid(), DateTime.UtcNow)
    {
    }

    /// <summary>Rehydrates an integration event while preserving its original identity.</summary>
    [JsonConstructor]
    public IntegrationEvent(Guid id, DateTime creationDate)
    {
        Id = id;
        CreationDate = creationDate;
    }

    /// <summary>Stable event identifier used across retries and transports.</summary>
    public Guid Id { get; private set; }

    /// <summary>UTC time at which the integration event was created.</summary>
    public DateTime CreationDate { get; private set; }
}
