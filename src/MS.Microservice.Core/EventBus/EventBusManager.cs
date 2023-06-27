using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MS.Microservice.Core.EventBus
{
    public class EventBusManager : IEventBus
    {
        private readonly ConcurrentDictionary<Type, List<object>> _eventHandlers;
        public EventBusManager()
        {
            _eventHandlers = new ConcurrentDictionary<Type, List<object>>();
        }
        public async Task PublishAsync<TEvent>(TEvent evt)
            where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    var eventHandler = handler as IEventHandler<TEvent>;
                    if (eventHandler == null)
                    {
                        throw new ArgumentException($"Handler {handler.GetType().Name} is not a valid event handler");
                    }
                    await eventHandler.Handle(evt);
                }
            }
        }

        public void Subscribe<TEvent, TEventHandler>(IEventHandler<TEvent> handler)
            where TEvent : IEvent
            where TEventHandler : IEventHandler<TEvent>
        {
            // TODO：兼容多个相同类型的事件订阅
            var eventType = typeof(TEvent); // 这里只能同一个类型的事件只能订阅一次
            if (!_eventHandlers.ContainsKey(eventType))
            {
                _eventHandlers[eventType] = new List<object>();
            }
            _eventHandlers[eventType].Add(handler);
        }

        public void UnSubscribe<TEvent, THandler>(IEventHandler<TEvent> handler)
            where THandler : IEventHandler<TEvent>
            where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);
            }
        }

        public IReadOnlyCollection<object> GetEventHandlers<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                return handlers.AsReadOnly();
            }
            return new List<object>();
        }
    }
}