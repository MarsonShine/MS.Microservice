using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Messaging.RabbitMQ;
using RawRabbitMqTransport = MS.Microservice.Messaging.RabbitMQ.RabbitMqTransport;
using RabbitMQ.Client;
using global::Wolverine;
using global::Wolverine.Configuration;
using global::Wolverine.RabbitMQ.Internal;
using global::Wolverine.Runtime;
using global::Wolverine.Transports;
using global::Wolverine.Transports.Sending;

namespace MS.Microservice.Messaging.Wolverine;

/// <summary>Send-only transport. Wolverine still owns durable sending agents, persistence and recovery.</summary>
public sealed class ConfirmedRabbitMqTransport : TransportBase<ConfirmedRabbitMqEndpoint>
{
    public const string Scheme = "ms-rabbitmq";
    private readonly Dictionary<Uri, ConfirmedRabbitMqEndpoint> _endpoints = [];
    public ConfirmedRabbitMqTransport() : base(Scheme, "Confirmed RabbitMQ", ["rabbitmq"]) { }
    protected override IEnumerable<ConfirmedRabbitMqEndpoint> endpoints() => _endpoints.Values;
    protected override ConfirmedRabbitMqEndpoint findEndpointByUri(Uri uri)
    {
        if (!_endpoints.TryGetValue(uri, out var endpoint)) _endpoints[uri] = endpoint = new(uri);
        return endpoint;
    }
}

public sealed class ConfirmedRabbitMqEndpoint : Endpoint
{
    public ConfirmedRabbitMqEndpoint(Uri uri) : base(uri, EndpointRole.Application) => Mode = EndpointMode.Durable;
    public override ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver)
        => throw new NotSupportedException("This transport sends only; use native RabbitMQ listeners.");
    protected override ISender CreateSender(IWolverineRuntime runtime)
        => new ConfirmedRabbitMqSender(Uri, runtime.Services.GetRequiredService<RawRabbitMqTransport>(),
            new RabbitMqEnvelopeMapper(this, runtime),
            runtime.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

internal sealed class ConfirmedRabbitMqSender(Uri destination, RawRabbitMqTransport transport,
    IRabbitMqEnvelopeMapper mapper, CancellationToken stoppingToken) : ISender
{
    public bool SupportsNativeScheduledSend => false;
    public Uri Destination => destination;
    public async Task<bool> PingAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { return await transport.ProbeAsync(timeout.Token); }
        catch (OperationCanceledException) { return false; }
    }

    public async ValueTask SendAsync(Envelope envelope)
    {
        var properties = new BasicProperties { Persistent = true, Headers = new Dictionary<string, object?>() };
        mapper.MapEnvelopeToOutgoing(envelope, properties);
        var routingKey = Uri.UnescapeDataString(destination.AbsolutePath.Trim('/'));
        await transport.SendEnvelopeConfirmedAsync(routingKey, properties,
            envelope.Data ?? throw new MessageContractException("Native envelope has no serialized data."), stoppingToken);
    }
}
