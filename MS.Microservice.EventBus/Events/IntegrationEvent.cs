using System.Text.Json.Serialization;

namespace MS.Microservice.EventBus.Events;

/// <summary>
/// Base contract for integration events exchanged through the event bus.
/// </summary>
public class IntegrationEvent
{
    public IntegrationEvent()
    {
        Id = Guid.NewGuid();
        CreationDate = DateTime.UtcNow;
    }

    [JsonConstructor]
    public IntegrationEvent(Guid id, DateTime creationDate)
    {
        Id = id;
        CreationDate = creationDate;
    }

    public Guid Id { get; private set; }

    public DateTime CreationDate { get; private set; }
}
