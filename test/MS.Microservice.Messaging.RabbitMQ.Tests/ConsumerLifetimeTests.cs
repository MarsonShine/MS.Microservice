using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace MS.Microservice.Messaging.RabbitMQ.Tests;

public sealed class ConsumerLifetimeTests
{
    [Fact]
    public async Task StoppingConsumerCancelsInFlightWork_AndDisposesTheUnacknowledgedChannel()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
        var registered = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.BasicConsumeAsync(Arg.Any<string>(), false, Arg.Any<string>(), false, false,
            Arg.Any<IDictionary<string, object?>?>(), Arg.Any<IAsyncBasicConsumer>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                registered.TrySetResult((AsyncEventingBasicConsumer)call.ArgAt<IAsyncBasicConsumer>(6));
                return Task.FromResult("test-consumer");
            });
        var receiver = Substitute.For<IMessageReceiver>();
        var handling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.ReceiveAsync(Arg.Any<SerializedMessage>(), "audit", Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                handling.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(2));
                return DeliveryResult.Acknowledge;
            });
        var options = new RabbitMqOptions();
        var topology = new MessageTopology([MessageContract.For<Changed>("profile.changed")],
            [MessageSubscription.For<Changed, Handler>("audit")]);
        using var service = new RabbitMqConsumerService(options, topology, factory,
            new(receiver, options, NullLogger<RabbitMqDeliveryHandler>.Instance), NullLogger<RabbitMqConsumerService>.Instance);
        await service.StartAsync(default);
        Task? delivery = null;
        try
        {
            var consumer = await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var encoded = RabbitMqWireCodec.Encode(new(Guid.NewGuid(), "profile.changed", 1, DateTimeOffset.UtcNow, "{}"), 1024);
            delivery = consumer.HandleBasicDeliverAsync("test-consumer", 42, false, options.Exchange,
                "profile.changed.v1", encoded.Properties, encoded.Body, default);
            await handling.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(timeout.Token);
        }
        if (delivery is not null) await delivery.WaitAsync(TimeSpan.FromSeconds(5));
        await channel.Received().DisposeAsync();
        await connection.Received().DisposeAsync();
        await channel.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
    private sealed class Handler : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
