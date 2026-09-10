namespace MS.Microservice.Messaging;

public sealed class MessageTopology
{
    public MessageContractRegistry Registry { get; }
    public IReadOnlyList<MessageSubscription> Subscriptions { get; }

    public MessageTopology(IEnumerable<MessageContract> contracts, IEnumerable<MessageSubscription> subscriptions)
    {
        Registry = new(contracts);
        var entries = subscriptions.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subscription in entries)
        {
            Registry.Get(subscription.MessageType);
            ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Consumer);
            if (subscription.Consumer.Length > 200 || !names.Add(subscription.Consumer))
                throw new ArgumentException("Consumer names must be unique and at most 200 characters.");
        }
        Subscriptions = Array.AsReadOnly(entries);
    }

    public MessageSubscription Subscription(string consumer) => Subscriptions.SingleOrDefault(x => x.Consumer == consumer)
        ?? throw new MessageContractException($"Unknown consumer: {consumer}.");
}

public sealed record MessagingProviderRegistration(string Name);
