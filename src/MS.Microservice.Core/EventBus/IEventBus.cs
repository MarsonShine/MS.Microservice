using System.Threading.Tasks;

public interface IEventBus
{
    void Subscribe<TEvent, TEventHandler>(IEventHandler<TEvent> handler)
        where TEventHandler : IEventHandler<TEvent>
        where TEvent : IEvent;
    void UnSubscribe<TEvent, TEventHandler>()
        where TEventHandler : IEventHandler<TEvent>
        where TEvent : IEvent;
    Task PublishAsync<TEvent>(TEvent evt) where TEvent : IEvent;
}