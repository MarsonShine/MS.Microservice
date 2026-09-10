using RabbitMQ.Client;
using System.Text;

namespace MS.Microservice.Messaging.RabbitMQ;

public sealed class RabbitMqOptions
{
    public string ConnectionString { get; set; } = "";
    public string Exchange { get; set; } = "ms.events";
    public string QueuePrefix { get; set; } = "ms.reference";
    public ushort PrefetchCount { get; set; } = 16;
    public ushort ConsumerConcurrency { get; set; } = 4;
    public int MaxMessageBytes { get; set; } = 1024 * 1024;
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps"))
            throw new ArgumentException("Messaging RabbitMQ connection must be an amqp/amqps URI supplied by the host.");
        if (string.IsNullOrWhiteSpace(Exchange) || string.IsNullOrWhiteSpace(QueuePrefix)
            || Encoding.UTF8.GetByteCount(Exchange) > 240 || Encoding.UTF8.GetByteCount(QueuePrefix) > 100
            || PrefetchCount == 0 || ConsumerConcurrency is < 1 or > 64
            || PrefetchCount < ConsumerConcurrency || MaxMessageBytes <= 0 || ReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentException("Invalid RabbitMQ topology, concurrency, or message limits.");
    }

    public string Queue(string consumer)
    {
        var name = $"{QueuePrefix}.{consumer}";
        if (Encoding.UTF8.GetByteCount(name) > 240) throw new ArgumentException("Consumer queue name is too long.");
        return name;
    }

    public ConnectionFactory CreateConnectionFactory() => new()
    {
        Uri = new Uri(ConnectionString), AutomaticRecoveryEnabled = false,
        ConsumerDispatchConcurrency = ConsumerConcurrency, RequestedHeartbeat = TimeSpan.FromSeconds(30),
        RequestedConnectionTimeout = TimeSpan.FromSeconds(10), ClientProvidedName = "MS.Microservice.SelfManaged"
    };
}
