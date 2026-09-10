using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace MS.Microservice.Messaging.RabbitMQ;

public sealed class RabbitMqTransport(RabbitMqOptions options, IConnectionFactory factory)
    : IMessageTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    public bool IsAvailable => _channel?.IsOpen == true;

    public async Task SendConfirmedAsync(SerializedMessage message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var encoded = RabbitMqWireCodec.Encode(message, options.MaxMessageBytes);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_channel?.IsOpen != true)
            {
                await ResetAsync();
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
                    publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
                await _channel.ExchangeDeclarePassiveAsync(options.Exchange, cancellationToken);
            }
            await _channel.BasicPublishAsync(options.Exchange, RabbitMqWireCodec.RoutingKey(message), mandatory: true,
                encoded.Properties, encoded.Body, cancellationToken);
        }
        catch (PublishException exception) when (exception.IsReturn)
        {
            throw new PermanentMessageException("unroutable_message");
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task ResetAsync()
    {
        var channel = _channel;
        var connection = _connection;
        _channel = null;
        _connection = null;
        try { if (channel is not null) await channel.DisposeAsync(); }
        finally { if (connection is not null) await connection.DisposeAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync();
        try { _disposed = true; await ResetAsync(); }
        finally { _gate.Release(); }
    }
}
