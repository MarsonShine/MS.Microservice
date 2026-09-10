using MS.Microservice.Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class BrokerConfirmationTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnroutableAndBrokerRejectedMessagesNeverReportConfirmed(bool nack)
    {
        var environment = await fixture.CreateEnvironmentAsync("SelfManaged");
        var options = environment.Broker;
        await using var connection = await options.CreateConnectionFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        if (nack)
        {
            var queue = await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true,
                arguments: new Dictionary<string, object?> { ["x-max-length"] = 0, ["x-overflow"] = "reject-publish" });
            await channel.QueueBindAsync(queue.QueueName, options.Exchange, "probe.changed.v1");
        }
        await using var transport = new RabbitMqTransport(options, options.CreateConnectionFactory());
        var message = ProbeHost.Topology().Registry.Serialize(ProbeHost.Message());
        if (nack)
        {
            var exception = await Assert.ThrowsAsync<PublishException>(() => transport.SendConfirmedAsync(message, default));
            Assert.False(exception.IsReturn);
        }
        else await Assert.ThrowsAsync<PermanentMessageException>(() => transport.SendConfirmedAsync(message, default));
    }

    [MessagingIntegrationTheory]
    [InlineData("confirmed-but-unobserved")]
    public async Task LostConfirmationRemainsAnUnknownResult(string _)
    {
        var environment = await fixture.CreateEnvironmentAsync("SelfManaged");
        var broker = environment.Broker;
        await using var control = await broker.CreateConnectionFactory().CreateConnectionAsync();
        await using var channel = await control.CreateChannelAsync();
        var sink = await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(sink.QueueName, broker.Exchange, "probe.changed.v1");
        var address = new Uri(broker.ConnectionString);
        var proxy = new ConfirmLossProxy(address.Host, address.Port);
        proxy.Start();
        var proxied = new RabbitMqOptions
        {
            ConnectionString = new UriBuilder(address) { Host = "127.0.0.1", Port = proxy.Port }.Uri.ToString(),
            Exchange = broker.Exchange, QueuePrefix = broker.QueuePrefix
        };
        await using var transport = new RabbitMqTransport(proxied, proxied.CreateConnectionFactory());
        var message = ProbeHost.Topology().Registry.Serialize(ProbeHost.Message());
        try
        {
            var publishing = transport.SendConfirmedAsync(message, default);
            await proxy.ConfirmObserved.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(publishing.IsCompleted);
            var delivered = await channel.BasicGetAsync(sink.QueueName, autoAck: false);
            Assert.NotNull(delivered);
            Assert.Equal(message.Id, Guid.Parse(delivered.BasicProperties.MessageId!));
            await channel.BasicAckAsync(delivered.DeliveryTag, multiple: false);
            await proxy.DisposeAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => publishing.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.True(publishing.IsCompleted, "The test deadline must not stand in for an observed transport failure.");
        }
        finally
        {
            // Disposal is idempotent at the test call site, including a failed barrier wait.
            await proxy.DisposeAsync();
        }
    }
}
