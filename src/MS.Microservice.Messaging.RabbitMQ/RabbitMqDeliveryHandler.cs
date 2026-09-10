using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace MS.Microservice.Messaging.RabbitMQ;

internal sealed class RabbitMqDeliveryHandler(IMessageReceiver receiver, RabbitMqOptions options,
    ILogger<RabbitMqDeliveryHandler> logger)
{
    public async Task HandleAsync(IChannel channel, ulong tag, IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body, string consumer, Action reconnect, CancellationToken cancellationToken)
    {
        DeliveryResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Decode synchronously: no callback-owned memory escapes into asynchronous business work.
            var message = RabbitMqWireCodec.Decode(properties, body, options.MaxMessageBytes);
            result = await receiver.ReceiveAsync(message, consumer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (MessageContractException)
        {
            logger.LogWarning("Rejected malformed message for {Consumer}", consumer);
            result = DeliveryResult.Reject;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Delivery deferred for {Consumer}: {ErrorType}", consumer, exception.GetType().Name);
            result = DeliveryResult.Requeue;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result == DeliveryResult.Requeue) await Task.Delay(options.ReconnectDelay, cancellationToken);
            if (result == DeliveryResult.Acknowledge)
                await channel.BasicAckAsync(tag, multiple: false, cancellationToken);
            else await channel.BasicNackAsync(tag, multiple: false, requeue: result == DeliveryResult.Requeue, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning("Acknowledgment connection failed: {ErrorType}", exception.GetType().Name);
            reconnect();
        }
    }
}
