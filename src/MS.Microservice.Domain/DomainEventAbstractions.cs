namespace MS.Microservice.Domain;

/// <summary>
/// Exposes domain events raised by an aggregate or entity without coupling the domain layer to a message bus.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Domain events currently buffered in memory.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Clears all buffered domain events after they have been safely handled.</summary>
    void ClearDomainEvents();
}

/// <summary>
/// Dispatches domain events raised inside the current process.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches the supplied domain events.
    /// </summary>
    /// <param name="domainEvents">Domain events to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes integration events to the outside world.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes an integration event.
    /// </summary>
    /// <param name="integrationEvent">The event object to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default);
}
