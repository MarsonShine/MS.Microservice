using System.Text;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace MS.Microservice.Messaging.RabbitMQ.Tests;

public sealed class WireAndPublisherTests
{
    [Theory]
    [InlineData("中文")]
    [InlineData("{\"name\":null}")]
    [InlineData("quotes & spaces")]
    public void WireRoundTripPreservesIdentityTimeAndBody(string payload)
    {
        var message = Message(payload);
        var encoded = RabbitMqWireCodec.Encode(message, 1024);
        var decoded = RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024);
        Assert.Equal(message.Id, decoded.Id);
        Assert.Equal(message.OccurredAtUtc, decoded.OccurredAtUtc);
        Assert.Equal(payload, decoded.Payload);
        Assert.True(encoded.Properties.Persistent);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("time")]
    public void InvalidMetadataIsRejected(string invalid)
    {
        var encoded = RabbitMqWireCodec.Encode(Message("{}"), 1024);
        if (invalid == "identity") encoded.Properties.MessageId = "invalid";
        if (invalid == "version") encoded.Properties.Headers!["ms-contract-version"] = -1;
        if (invalid == "time") encoded.Properties.Headers!["ms-occurred-at"] = "invalid";
        Assert.Throws<MessageContractException>(() => RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024));
    }

    [Fact]
    public void LimitsAreMeasuredInUtf8Bytes()
        => Assert.Throws<PermanentMessageException>(() => RabbitMqWireCodec.Encode(Message("中文"), 3));

    [Theory]
    [InlineData("correlation", 201)]
    [InlineData("traceparent", 129)]
    [InlineData("tracestate", 513)]
    public void OversizedMetadataIsRejectedOnBothWireDirections(string field, int length)
    {
        var value = new string('x', length);
        var message = Message("{}");
        var oversized = field switch
        {
            "correlation" => message with { CorrelationId = value },
            "traceparent" => message with { TraceParent = value },
            _ => message with { TraceState = value }
        };
        Assert.Throws<PermanentMessageException>(() => RabbitMqWireCodec.Encode(oversized, 1024));
        var encoded = RabbitMqWireCodec.Encode(message, 1024);
        if (field == "correlation") encoded.Properties.CorrelationId = value;
        else encoded.Properties.Headers![field] = value;
        Assert.Throws<MessageContractException>(() => RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024));
    }

    [Theory]
    [InlineData(85, true)]
    [InlineData(86, false)]
    public void CorrelationIdAlsoRespectsAmqpUtf8ByteLimit(int characters, bool valid)
    {
        var correlationId = new string('中', characters);
        var message = Message("{}") with { CorrelationId = correlationId };
        if (valid)
        {
            var encoded = RabbitMqWireCodec.Encode(message, 1024);
            Assert.Equal(correlationId, RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024).CorrelationId);
        }
        else
        {
            Assert.Throws<PermanentMessageException>(() => RabbitMqWireCodec.Encode(message, 1024));
            var encoded = RabbitMqWireCodec.Encode(Message("{}"), 1024);
            encoded.Properties.CorrelationId = correlationId;
            Assert.Throws<MessageContractException>(() => RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024));
        }
    }

    [Fact]
    public void WirePreservesValidDatabaseLengthBoundaries()
    {
        var message = Message("{}") with
        {
            CorrelationId = new string('a', 200), TraceParent = new string('中', 128),
            TraceState = new string('中', 512)
        };
        var encoded = RabbitMqWireCodec.Encode(message, 1024);
        var decoded = RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024);
        Assert.Equal(message.CorrelationId, decoded.CorrelationId);
        Assert.Equal(message.TraceParent, decoded.TraceParent);
        Assert.Equal(message.TraceState, decoded.TraceState);
    }

    [Fact]
    public void Utf8EncodedExternalTraceHeaderMustFitDatabaseColumn()
    {
        var encoded = RabbitMqWireCodec.Encode(Message("{}"), 1024);
        encoded.Properties.Headers!["tracestate"] = Encoding.UTF8.GetBytes(new string('中', 513));
        Assert.Throws<MessageContractException>(() => RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024));
    }

    [Fact]
    public async Task PublishDoesNotCompleteBeforeBrokerConfirmation()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(_ => new ValueTask(confirmed.Task));
        await using var transport = new RabbitMqTransport(new(), factory);
        var message = Message("{}");
        var sending = transport.SendConfirmedAsync(message, default);
        Assert.False(sending.IsCompleted);
        confirmed.SetResult();
        await sending;
        await connection.Received().CreateChannelAsync(Arg.Is<CreateChannelOptions>(x =>
            x.PublisherConfirmationsEnabled && x.PublisherConfirmationTrackingEnabled), Arg.Any<CancellationToken>());
        await channel.Received().BasicPublishAsync("ms.events", "profile.changed.v1", true,
            Arg.Is<BasicProperties>(x => x.Persistent && x.MessageId == message.Id.ToString("N")),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrokerRejectionsCannotBecomeSuccessfulPublication(bool returned)
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
        channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new PublishException(1, returned)));
        await using var transport = new RabbitMqTransport(new(), factory);
        if (returned) await Assert.ThrowsAsync<PermanentMessageException>(() => transport.SendConfirmedAsync(Message("{}"), default));
        else await Assert.ThrowsAsync<PublishException>(() => transport.SendConfirmedAsync(Message("{}"), default));
    }

    [Fact]
    public void InvalidUtf8HeadersArePermanentContractErrors()
    {
        var encoded = RabbitMqWireCodec.Encode(Message("{}"), 1024);
        encoded.Properties.Headers!["ms-contract-name"] = new byte[] { 0xff };
        Assert.Throws<MessageContractException>(() => RabbitMqWireCodec.Decode(encoded.Properties, encoded.Body, 1024));
    }

    [Fact]
    public async Task CancellationWhileWaitingForConfirmationPropagatesAndClosesTheChannel()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
        channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(5))));
        await using var transport = new RabbitMqTransport(new(), factory);
        using var cancellation = new CancellationTokenSource();
        var sending = transport.SendConfirmedAsync(Message("{}"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        Assert.False(transport.IsAvailable);
        await channel.Received().DisposeAsync();
    }

    private static SerializedMessage Message(string payload) => new(Guid.NewGuid(), "profile.changed", 1,
        DateTimeOffset.UtcNow, payload, "correlation");
}
