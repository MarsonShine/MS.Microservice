using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus.Abstractions;

/// <summary>
/// Defines the event bus contract for publishing and managing integration event subscriptions.
/// </summary>
public interface IEventBus
{
	void Publish(IntegrationEvent @event);

	/// <summary>Publishes an event. Existing synchronous adapters use their Publish implementation.</summary>
	Task PublishAsync(IntegrationEvent @event)
	{
		Publish(@event);
		return Task.CompletedTask;
	}

	void Subscribe<TEvent, THandler>()
		where TEvent : IntegrationEvent
		where THandler : IIntegrationEventHandler<TEvent>;

	void Unsubscribe<TEvent, THandler>()
		where THandler : IIntegrationEventHandler<TEvent>
		where TEvent : IntegrationEvent;
}

/// <summary>
/// Legacy alias kept for backward compatibility. Prefer <see cref="IEventBus"/>.
/// </summary>
public interface IEventbus : IEventBus
{
}
