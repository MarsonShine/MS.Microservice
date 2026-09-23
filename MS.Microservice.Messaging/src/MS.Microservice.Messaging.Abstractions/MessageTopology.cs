namespace MS.Microservice.Messaging;

public sealed class MessageTopology
{
    private readonly Dictionary<string, MessageSubscription> _byConsumer = new(StringComparer.Ordinal);
    public MessageContractRegistry Registry { get; }
    public IReadOnlyList<MessageSubscription> Subscriptions { get; }

    public MessageTopology(IEnumerable<MessageContract> contracts, IEnumerable<MessageSubscription> subscriptions)
    {
        Registry = new(contracts);
        var entries = subscriptions.ToArray();
        foreach (var subscription in entries)
        {
            Registry.Get(subscription.MessageType);
            ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Consumer);
            if (subscription.Consumer.Length > 200 || !_byConsumer.TryAdd(subscription.Consumer, subscription))
                throw new ArgumentException("Consumer names must be unique and at most 200 characters.");
        }
        Subscriptions = Array.AsReadOnly(entries);
    }

    public MessageSubscription Subscription(string consumer) => consumer is not null && _byConsumer.TryGetValue(consumer, out var subscription)
        ? subscription
        : throw new MessageContractException($"Unknown consumer: {consumer}.");
}

public sealed record MessagingProviderRegistration(string Name);
