using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

public class EventBusManager : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<object>> _eventHandlers;
    public EventBusManager() {
        _eventHandlers = new ConcurrentDictionary<Type, List<object>>();
    }
    public Task PublishAsync<TEvent>(TEvent evt)
        where TEvent : IEvent
    {
        throw new System.NotImplementedException();
    }

    public void Subscribe<TEvent, TEventHandler>(IEventHandler<TEvent> handler)
        where TEvent : IEvent
        where TEventHandler : IEventHandler<TEvent>
    {
        var eventType = typeof(TEvent);
        if (!_eventHandlers.ContainsKey(eventType))
        {
            _eventHandlers[eventType] = new List<object>();
        }
        _eventHandlers[eventType].Add(handler);
    }

    public void UnSubscribe<TEvent, THandler>() 
        where THandler : IEventHandler<TEvent>
        where TEvent : IEvent
    {
        throw new System.NotImplementedException();
    }
}