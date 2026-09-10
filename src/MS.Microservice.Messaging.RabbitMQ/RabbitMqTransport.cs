using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Text;

namespace MS.Microservice.Messaging.RabbitMQ;

public sealed class RabbitMqTransport(RabbitMqOptions options, IConnectionFactory factory)
    : IMessageTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    public bool IsAvailable => _channel?.IsOpen == true;

    public Task SendConfirmedAsync(SerializedMessage message, CancellationToken cancellationToken)
    {
        var encoded = RabbitMqWireCodec.Encode(message, options.MaxMessageBytes);
        return SendEnvelopeConfirmedAsync(RabbitMqWireCodec.RoutingKey(message), encoded.Properties, encoded.Body, cancellationToken);
    }

    /// <summary>Confirms an adapter-owned envelope while preserving that framework's wire metadata.</summary>
    public async Task SendEnvelopeConfirmedAsync(string routingKey, BasicProperties properties, ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (body.Length > options.MaxMessageBytes || Encoding.UTF8.GetByteCount(routingKey) >= 255)
            throw new PermanentMessageException("message_limits_exceeded");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureChannelAsync(cancellationToken);
            properties.Persistent = true;
            await _channel!.BasicPublishAsync(options.Exchange, routingKey, mandatory: true,
                properties, body, cancellationToken);
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

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try { await EnsureChannelAsync(cancellationToken); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { await ResetAsync(); return false; }
        finally { _gate.Release(); }
    }

    private async Task EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen == true) return;
        await ResetAsync();
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        await _channel.ExchangeDeclarePassiveAsync(options.Exchange, cancellationToken);
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
