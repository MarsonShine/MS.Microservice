namespace MS.Microservice.EventBus.Events;

/// <summary>
/// Compatibility base for existing EventBus consumers. New contracts should derive from
/// <see cref="Core.Messaging.IntegrationEvent" /> directly.
/// </summary>
public class IntegrationEvent : Core.Messaging.IntegrationEvent
{
    public IntegrationEvent()
    {
    }

    [System.Text.Json.Serialization.JsonConstructor]
    public IntegrationEvent(Guid id, DateTime creationDate)
        : base(id, creationDate)
    {
    }
}
