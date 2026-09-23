using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace MS.Microservice.Messaging.RabbitMQ.Tests;

public sealed class DeliveryTests
{
    [Theory]
    [InlineData(DeliveryResult.Acknowledge)]
    [InlineData(DeliveryResult.Requeue)]
    [InlineData(DeliveryResult.Reject)]
    public async Task ConsumerOutcomeControlsSingleDeliveryAcknowledgment(DeliveryResult result)
    {
        var receiver = Substitute.For<IMessageReceiver>();
        receiver.ReceiveAsync(Arg.Any<SerializedMessage>(), "audit", Arg.Any<CancellationToken>()).Returns(result);
        var channel = Substitute.For<IChannel>();
        var delivery = Encoded();
        await Handler(receiver).HandleAsync(channel, 42, delivery.Properties, delivery.Body, "audit", () => { }, default);
        if (result == DeliveryResult.Acknowledge)
        {
            await channel.Received().BasicAckAsync(42, false, Arg.Any<CancellationToken>());
            await channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }
        else
        {
            await channel.Received().BasicNackAsync(42, false, result == DeliveryResult.Requeue, Arg.Any<CancellationToken>());
            await channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task CancellationLeavesDeliveryUnacknowledged()
    {
        var receiver = Substitute.For<IMessageReceiver>();
        var channel = Substitute.For<IChannel>();
        var delivery = Encoded();
        await Handler(receiver).HandleAsync(channel, 1, delivery.Properties, delivery.Body, "audit", () => { }, new(true));
        await channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LostAckRequestsConnectionRecovery()
    {
        var receiver = Substitute.For<IMessageReceiver>();
        receiver.ReceiveAsync(Arg.Any<SerializedMessage>(), "audit", Arg.Any<CancellationToken>()).Returns(DeliveryResult.Acknowledge);
        var channel = Substitute.For<IChannel>();
        channel.BasicAckAsync(Arg.Any<ulong>(), false, Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new IOException("disconnected")));
        var delivery = Encoded();
        var reconnect = false;
        await Handler(receiver).HandleAsync(channel, 1, delivery.Properties, delivery.Body, "audit", () => reconnect = true, default);
        Assert.True(reconnect);
    }

    [Fact]
    public async Task MalformedMessagesAreRejectedBeforeBusinessDispatch()
    {
        var receiver = Substitute.For<IMessageReceiver>();
        var channel = Substitute.For<IChannel>();
        var delivery = Encoded();
        delivery.Properties.MessageId = "invalid";
        await Handler(receiver).HandleAsync(channel, 1, delivery.Properties, delivery.Body, "audit", () => { }, default);
        await receiver.DidNotReceive().ReceiveAsync(Arg.Any<SerializedMessage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await channel.Received().BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("correlation")]
    [InlineData("traceparent")]
    [InlineData("tracestate")]
    public async Task OversizedMetadataIsRejectedWithoutRequeueOrBusinessDispatch(string field)
    {
        var receiver = Substitute.For<IMessageReceiver>();
        var channel = Substitute.For<IChannel>();
        var delivery = Encoded();
        if (field == "correlation") delivery.Properties.CorrelationId = new string('x', 201);
        else delivery.Properties.Headers![field] = new string('x', field == "traceparent" ? 129 : 513);
        await Handler(receiver).HandleAsync(channel, 1, delivery.Properties, delivery.Body, "audit", () => { }, default);
        await receiver.DidNotReceive().ReceiveAsync(Arg.Any<SerializedMessage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await channel.Received().BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
    }

    private static RabbitMqDeliveryHandler Handler(IMessageReceiver receiver)
        => new(receiver, new() { ReconnectDelay = TimeSpan.FromMilliseconds(1) }, NullLogger<RabbitMqDeliveryHandler>.Instance);
    private static (BasicProperties Properties, byte[] Body) Encoded()
        => RabbitMqWireCodec.Encode(new(Guid.NewGuid(), "profile.changed", 1, DateTimeOffset.UtcNow, "{}"), 1024);
}
