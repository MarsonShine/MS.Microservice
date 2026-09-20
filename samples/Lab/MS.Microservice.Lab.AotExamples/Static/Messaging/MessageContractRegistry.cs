using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using MS.Microservice.Messaging;

namespace MS.Microservice.Lab.AotExamples.Static.Messaging;

public sealed class MessageContract
{
    private MessageContract(Type messageType, string name, int version,
        Func<IIntegrationEvent, string> serialize, Func<string, IIntegrationEvent?> deserialize)
        => (MessageType, Name, Version, Serialize, Deserialize) = (messageType, name, version, serialize, deserialize);

    public Type MessageType { get; }
    public string Name { get; }
    public int Version { get; }
    internal Func<IIntegrationEvent, string> Serialize { get; }
    internal Func<string, IIntegrationEvent?> Deserialize { get; }

    /// <summary>Registers a contract with explicit metadata, normally supplied by a generated JSON context.</summary>
    public static MessageContract For<T>(string name, JsonTypeInfo<T> typeInfo, int version = 1)
        where T : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return new(typeof(T), name, version,
            message => JsonSerializer.Serialize((T)message, typeInfo),
            payload => JsonSerializer.Deserialize(payload, typeInfo));
    }
}

/// <summary>Immutable allowlist used by both persistence writers and transport readers.</summary>
public sealed class MessageContractRegistry
{
    private readonly Dictionary<Type, MessageContract> _byType = [];
    private readonly Dictionary<(string Name, int Version), MessageContract> _byName = [];

    public MessageContractRegistry(IEnumerable<MessageContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        foreach (var contract in contracts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contract.Name);
            if (contract.Version <= 0 || contract.Name.Length > 200
                || !typeof(IIntegrationEvent).IsAssignableFrom(contract.MessageType)
                || contract.MessageType.IsAbstract || contract.MessageType.ContainsGenericParameters)
                throw new ArgumentException($"Invalid message contract: {contract.Name} v{contract.Version}.");
            if (!_byType.TryAdd(contract.MessageType, contract)
                || !_byName.TryAdd((contract.Name, contract.Version), contract))
                throw new ArgumentException($"Duplicate message contract: {contract.Name} v{contract.Version}.");
        }
    }

    public IReadOnlyCollection<MessageContract> Contracts => _byType.Values;

    public MessageContract Get(Type type) => _byType.TryGetValue(type, out var contract)
        ? contract : throw new MessageContractException($"Unregistered event type: {type.Name}.");

    public MessageContract Get(string name, int version) => _byName.TryGetValue((name, version), out var contract)
        ? contract : throw new MessageContractException($"Unsupported event contract: {name} v{version}.");

    public SerializedMessage Serialize(IIntegrationEvent message, MessageContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateIdentity(message.Id, message.OccurredAtUtc);
        var contract = Get(message.GetType());
        return new(message.Id, contract.Name, contract.Version, message.OccurredAtUtc,
            contract.Serialize(message), context?.CorrelationId,
            context?.TraceParent, context?.TraceState);
    }

    public IIntegrationEvent Deserialize(SerializedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateIdentity(message.Id, message.OccurredAtUtc);
        var contract = Get(message.ContractName, message.ContractVersion);
        try
        {
            var value = contract.Deserialize(message.Payload)
                ?? throw new MessageContractException("Event payload is null.");
            if (value.Id != message.Id || value.OccurredAtUtc != message.OccurredAtUtc)
                throw new MessageContractException("Event identity does not match its envelope.");
            return value;
        }
        catch (JsonException exception)
        {
            // Payload is intentionally excluded from diagnostics.
            throw new MessageContractException("Invalid event JSON.", exception);
        }
    }

    private static void ValidateIdentity(Guid id, DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty || occurredAt == default || occurredAt.Offset != TimeSpan.Zero)
            throw new MessageContractException("Events require a nonempty Id and a UTC occurrence time.");
    }
}
