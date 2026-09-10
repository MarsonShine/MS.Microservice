using System.Text.Json;

namespace MS.Microservice.Messaging;

public sealed record MessageContract(Type MessageType, string Name, int Version)
{
    public static MessageContract For<T>(string name, int version = 1) where T : IIntegrationEvent
        => new(typeof(T), name, version);
}

/// <summary>Immutable allowlist used by both persistence writers and transport readers.</summary>
public sealed class MessageContractRegistry
{
    private readonly Dictionary<Type, MessageContract> _byType = [];
    private readonly Dictionary<(string Name, int Version), MessageContract> _byName = [];
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

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
            JsonSerializer.Serialize(message, contract.MessageType, _json), context?.CorrelationId,
            context?.TraceParent, context?.TraceState);
    }

    public IIntegrationEvent Deserialize(SerializedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateIdentity(message.Id, message.OccurredAtUtc);
        var contract = Get(message.ContractName, message.ContractVersion);
        try
        {
            var value = (IIntegrationEvent?)JsonSerializer.Deserialize(message.Payload, contract.MessageType, _json)
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
