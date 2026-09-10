using System.Text;
using MS.Microservice.Messaging.RabbitMQ;
using RawRabbitMqTransport = MS.Microservice.Messaging.RabbitMQ.RabbitMqTransport;
using NSubstitute;
using RabbitMQ.Client;
using global::Wolverine;
using global::Wolverine.RabbitMQ.Internal;
using Xunit;

namespace MS.Microservice.Messaging.Wolverine.Tests;

public sealed class ConfirmedSenderTests
{
    [Fact]
    public async Task NativeEnvelopesUseMandatoryConfirmedPublishingAndPreserveHeaders()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(_ => new ValueTask(confirmation.Task));
        await using var transport = new RawRabbitMqTransport(new(), connectionFactory);
        var mapper = Substitute.For<IRabbitMqEnvelopeMapper>();
        mapper.When(x => x.MapEnvelopeToOutgoing(Arg.Any<Envelope>(), Arg.Any<IBasicProperties>())).Do(call =>
        {
            var properties = call.Arg<IBasicProperties>();
            properties.Type = "profile.changed.v1";
            properties.Headers!["native-header"] = "preserved";
        });
        var sender = new ConfirmedRabbitMqSender(new("ms-rabbitmq://exchange/profile.changed.v1"), transport, mapper, default);
        var envelope = new Envelope { Data = Encoding.UTF8.GetBytes("{}") };
        var sending = sender.SendAsync(envelope).AsTask();
        Assert.False(sending.IsCompleted);
        confirmation.SetResult();
        await sending;
        await channel.Received().BasicPublishAsync("ms.events", "profile.changed.v1", true,
            Arg.Is<BasicProperties>(x => x.Persistent && x.Type == "profile.changed.v1"
                && (string)x.Headers!["native-header"]! == "preserved"),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }
}
