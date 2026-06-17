using MS.Microservice.Domain;
using Wolverine;

namespace MS.Microservice.Infrastructure.Messaging;

/// <summary>Wolverine-backed domain event dispatcher.</summary>
public sealed class WolverineDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMessageBus _messageBus;

    /// <summary>Initializes a new instance of <see cref="WolverineDomainEventDispatcher" />.</summary>
    public WolverineDomainEventDispatcher(IMessageBus messageBus)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _messageBus.PublishAsync(domainEvent).ConfigureAwait(false);
        }
    }
}

/// <summary>Wolverine-backed integration event publisher.</summary>
public sealed class WolverineIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IMessageBus _messageBus;

    /// <summary>Initializes a new instance of <see cref="WolverineIntegrationEventPublisher" />.</summary>
    public WolverineIntegrationEventPublisher(IMessageBus messageBus)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
    }

    /// <inheritdoc />
    public async Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        cancellationToken.ThrowIfCancellationRequested();
        await _messageBus.PublishAsync(integrationEvent).ConfigureAwait(false);
    }
}
