using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.EventEntity
{
    public class InMemoryEventBus : IEventBus
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<Type> _eventTypes;
        private readonly Dictionary<string, List<Type>> _handles;
        public InMemoryEventBus(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _eventTypes = new List<Type>();
            _handles = new Dictionary<string, List<Type>>();
        }

        public void Publish(EventBase @event)
        {
            Check.NotNull(@event, nameof(@event));
            var eventName = @event.GetType().Name;

            var eventHandleTypes = GetHandlesByEventName(eventName);
            foreach (var eventHandleType in eventHandleTypes)
            {
                var concreteType = typeof(IEventHandle<>).MakeGenericType(@event.GetType());
                var handleInstance = _serviceProvider.GetService(eventHandleType);
                if (handleInstance == null) continue;

                ((Task)concreteType.GetMethod("Handle")!.Invoke(handleInstance, new object[] { @event })!)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
        }

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : EventBase
        {
            Check.NotNull(@event, nameof(@event));

            var handleInstance = _serviceProvider.GetService<IEventHandle<TEvent>>();
            if (handleInstance == null) return;
            await handleInstance.Handle(@event);
        }

        private IEnumerable<Type> GetHandlesByEventName(string eventName) => _handles[eventName];

        public void Subscribe<TEvent, TEventHandle>()
            where TEvent : EventBase
            where TEventHandle : IEventHandle<TEvent>
        {
            var eventName = GetEventName<TEvent>();
            AddEventHandle(typeof(TEventHandle), eventName);

            if (!_eventTypes.Contains(typeof(TEvent)))
                _eventTypes.Add(typeof(TEvent));
        }

        private string GetEventName<TEvent>()
        {
            return typeof(TEvent).Name;
        }

        private void AddEventHandle(Type eventHandleType, string eventName)
        {
            if (!_handles.ContainsKey(eventName))
            {
                _handles.Add(eventName, new List<Type>());
            }
            if (_handles[eventName].Any(handleType => handleType == eventHandleType))
                throw new ArgumentException($"事件 {eventHandleType.Name} 已经注册事件 {eventName}", nameof(eventHandleType));

            _handles[eventName].Add(eventHandleType);
        }
    }
}
