using RabbitMQ.Client;

namespace MS.Microservice.Messaging.RabbitMQ;

public sealed class RabbitMqTopologyProvisioner(RabbitMqOptions options, MessageTopology topology, IConnectionFactory factory)
{
    /// <summary>Explicit administrative operation; application startup only checks existing topology.</summary>
    public async Task ProvisionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(options.Exchange + ".dead", ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        foreach (var subscription in topology.Subscriptions)
        {
            var queue = options.Queue(subscription.Consumer);
            var dead = queue + ".dead";
            await channel.QueueDeclareAsync(dead, durable: true, exclusive: false, autoDelete: false,
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(dead, options.Exchange + ".dead", dead, cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "classic",
                    ["x-dead-letter-exchange"] = options.Exchange + ".dead",
                    ["x-dead-letter-routing-key"] = dead
                }, cancellationToken: cancellationToken);
            var contract = topology.Registry.Get(subscription.MessageType);
            await channel.QueueBindAsync(queue, options.Exchange, MessageRoutingKey.Format(contract.Name, contract.Version),
                cancellationToken: cancellationToken);
        }
    }
}
