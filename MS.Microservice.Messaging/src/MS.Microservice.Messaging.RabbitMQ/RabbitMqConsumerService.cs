using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MS.Microservice.Messaging.RabbitMQ;

internal sealed class RabbitMqConsumerService(RabbitMqOptions options, MessageTopology topology,
    IConnectionFactory factory, RabbitMqDeliveryHandler deliveries, ILogger<RabbitMqConsumerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (topology.Subscriptions.Count == 0) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConsumeSessionAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("RabbitMQ consumption unavailable: {ErrorType}", exception.GetType().Name); }
            try { await Task.Delay(options.ReconnectDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ConsumeSessionAsync(CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await using var connection = await factory.CreateConnectionAsync(session.Token);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: options.ConsumerConcurrency), session.Token);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        connection.ConnectionShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        try
        {
            await channel.ExchangeDeclarePassiveAsync(options.Exchange, session.Token);
            await channel.BasicQosAsync(0, options.PrefetchCount, global: false, session.Token);
            foreach (var subscription in topology.Subscriptions)
            {
                var queue = options.Queue(subscription.Consumer);
                await channel.QueueDeclarePassiveAsync(queue, session.Token);
                await channel.QueueDeclarePassiveAsync(queue + ".dead", session.Token);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => deliveries.HandleAsync(channel, delivery.DeliveryTag,
                    delivery.BasicProperties, delivery.Body, subscription.Consumer,
                    () => disconnected.TrySetResult(), session.Token);
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, session.Token);
            }
            await disconnected.Task.WaitAsync(session.Token);
        }
        finally
        {
            await session.CancelAsync();
            // Disposing this dedicated channel requeues unacknowledged deliveries. BasicCancel alone would not.
        }
    }
}
