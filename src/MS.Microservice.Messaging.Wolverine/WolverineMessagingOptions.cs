using Npgsql;
using MS.Microservice.Messaging.RabbitMQ;

namespace MS.Microservice.Messaging.Wolverine;

public sealed class WolverineMessagingOptions
{
    public string ConnectionString { get; set; } = "";
    public string BrokerConnectionString { get; set; } = "";
    public string Schema { get; set; } = "wolverine";
    public string Exchange { get; set; } = "ms.events";
    public string QueuePrefix { get; set; } = "ms.reference";
    public string ServiceName { get; set; } = "MS.Microservice.Reference";
    public int MaxRetryAttempts { get; set; } = 10;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ProcessedRetention { get; set; } = TimeSpan.FromDays(30);

    internal RabbitMqOptions BrokerOptions() => new()
    {
        ConnectionString = BrokerConnectionString, Exchange = Exchange, QueuePrefix = QueuePrefix
    };

    public void Validate()
    {
        BrokerOptions().Validate();
        var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
            || !System.Text.RegularExpressions.Regex.IsMatch(Schema, "^[a-z_][a-z0-9_]{0,62}$")
            || string.IsNullOrWhiteSpace(ServiceName) || MaxRetryAttempts is < 0 or > 100
            || ProcessingTimeout <= TimeSpan.Zero || ProcessedRetention <= TimeSpan.Zero)
            throw new ArgumentException("Wolverine requires a business database, safe schema, service identity and positive durations.");
    }
}
