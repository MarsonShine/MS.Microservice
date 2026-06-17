namespace MS.Microservice.Domain.Events;

/// <summary>
/// No-op domain event dispatcher for design-time, tests, and hosts that do not dispatch domain events.
/// </summary>
public sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    /// <inheritdoc />
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>No-op integration event publisher.</summary>
public sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
