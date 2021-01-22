using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.EventBus
{
    public class InMemoryEventBusSubscriptionsManager : IEventBusSubscriptionsManager
    {
        private readonly Dictionary<string, List<SubscriptionDescriptionInfo>> _handlers;
        private readonly List<Type> _eventTypes;
        public InMemoryEventBusSubscriptionsManager()
        {
            _handlers = new Dictionary<string, List<SubscriptionDescriptionInfo>>();
            _eventTypes = new List<Type>();
        }

        public void AddSubscription<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            // 获取事件名称
            var eventName = GetEventKey<T>();
            AddSubscription(typeof(TH), eventName);
            if (!_eventTypes.Contains(typeof(T)))
            {
                _eventTypes.Add(typeof(T));
            }
        }

        private void AddSubscription(Type handlerType, string eventName)
        {
            if (!HasSubscriptionsForEvent(eventName))
            {
                _handlers.Add(eventName, new List<SubscriptionDescriptionInfo>());
            }
            if (_handlers[eventName].Any(s => s.HandlerType == handlerType))
            {
                throw new ArgumentException(
                    $"Handler Type {handlerType.Name} already registered for '{eventName}'", nameof(handlerType));
            }
            _handlers[eventName].Add(new SubscriptionDescriptionInfo(handlerType));
        }

        private bool HasSubscriptionsForEvent(string eventName) => _handlers.ContainsKey(eventName);

        public void Clear()
        {
            _handlers.Clear();
            _eventTypes.Clear();
        }

        public string GetEventKey<T>() => typeof(T).Name;

        public Type GetEventTypeByName(string eventName) => _eventTypes.SingleOrDefault(t => t.Name == eventName)!;

        public void RemoveSubscription<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = GetEventKey<T>();
            var eventHandlerToRemove = FindSubscriptionToRemove(eventName, typeof(TH));
            RemoveHandler<T>(eventName, eventHandlerToRemove);
        }

        private void RemoveHandler<T>(string eventName, SubscriptionDescriptionInfo? eventHandlerToRemove)
        {
            if (eventHandlerToRemove != null)
            {
                _handlers[eventName].Remove(eventHandlerToRemove);
                if (!_handlers[eventName].Any())
                {
                    _handlers.Remove(eventName);
                    var eventType = _eventTypes.SingleOrDefault(t => t == typeof(T));
                    if (eventType != null)
                    {
                        _eventTypes.Remove(eventType);
                    }
                    // 这里可以触发删除事件的回调事件
                }
            }
        }

        private SubscriptionDescriptionInfo? FindSubscriptionToRemove(string eventName, Type handlerType)
        {
            if (!HasSubscriptionsForEvent(eventName))
            {
                return null;
            }
            return _handlers[eventName].SingleOrDefault(p => p.HandlerType == handlerType);
        }
    }
}
