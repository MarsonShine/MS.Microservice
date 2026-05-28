using MS.Microservice.Domain.EventSourcing;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MS.Microservice.Infrastructure.EventSourcing.Serialization
{
    public sealed class EventTypeRegistry
    {
        private readonly Dictionary<string, Type> _eventTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Type, string> _eventNames = new();

        public EventTypeRegistry Register<TEvent>(string? eventType = null)
            where TEvent : class, IEventSourcedEvent
        {
            var eventClrType = typeof(TEvent);
            var name = string.IsNullOrWhiteSpace(eventType) ? eventClrType.Name : eventType;
            _eventTypes[name] = eventClrType;
            _eventNames[eventClrType] = name;
            return this;
        }

        public Type Resolve(string eventType)
            => _eventTypes.TryGetValue(eventType, out var clrType)
                ? clrType
                : throw new InvalidOperationException($"未注册事件类型：{eventType}");

        public string Resolve(Type eventType)
            => _eventNames.TryGetValue(eventType, out var eventName)
                ? eventName
                : throw new InvalidOperationException($"未注册事件 CLR 类型：{eventType.FullName}");
    }

    public sealed record SerializedEvent(string EventType, string Payload, string Metadata);

    public sealed class SystemTextJsonEventSerializer
    {
        private readonly EventTypeRegistry _eventTypeRegistry;
        private readonly JsonSerializerOptions _serializerOptions;

        public SystemTextJsonEventSerializer(EventTypeRegistry eventTypeRegistry, JsonSerializerOptions? serializerOptions = null)
        {
            _eventTypeRegistry = eventTypeRegistry;
            _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        }

        public SerializedEvent Serialize<TEvent>(TEvent @event, EventMetadata metadata)
            where TEvent : class, IEventSourcedEvent
        {
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(metadata);

            return new SerializedEvent(
                _eventTypeRegistry.Resolve(@event.GetType()),
                JsonSerializer.Serialize(@event, @event.GetType(), _serializerOptions),
                JsonSerializer.Serialize(metadata, _serializerOptions));
        }

        public TEvent Deserialize<TEvent>(string eventType, string payload)
            where TEvent : class, IEventSourcedEvent
        {
            var clrType = _eventTypeRegistry.Resolve(eventType);
            var result = JsonSerializer.Deserialize(payload, clrType, _serializerOptions)
                ?? throw new InvalidOperationException($"事件 {eventType} 反序列化结果为空。");

            return result as TEvent
                ?? throw new InvalidOperationException($"事件 {eventType} 无法转换为 {typeof(TEvent).Name}。");
        }

        public EventMetadata DeserializeMetadata(string metadata)
            => string.IsNullOrWhiteSpace(metadata)
                ? new EventMetadata()
                : JsonSerializer.Deserialize<EventMetadata>(metadata, _serializerOptions) ?? new EventMetadata();

        public string SerializeState<TState>(TState state)
            => JsonSerializer.Serialize(state, _serializerOptions);

        public TState DeserializeState<TState>(string state)
            => JsonSerializer.Deserialize<TState>(state, _serializerOptions)
                ?? throw new InvalidOperationException($"状态 {typeof(TState).Name} 反序列化失败。");
    }
}
