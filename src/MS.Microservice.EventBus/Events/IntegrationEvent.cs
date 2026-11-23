namespace MS.Microservice.EventBus.Events;

/// <summary>
/// Base event accepted by this EventBus, with identity and timestamp supplied by
/// <see cref="Core.Messaging.IntegrationEvent" />.
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
