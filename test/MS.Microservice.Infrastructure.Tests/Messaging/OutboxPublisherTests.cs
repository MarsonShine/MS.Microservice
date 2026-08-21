using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Persistence.EFCore.Outbox;
using NSubstitute;
using Wolverine;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Messaging;

public sealed class OutboxPublisherTests
{
    [Fact]
    public async Task PublishBatchAsync_WhenPublishSucceeds_ConfirmsOwnedMessage()
    {
        var store = Substitute.For<IOutboxStore>();
        var bus = Substitute.For<IMessageBus>();
        bus.PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions?>()).Returns(ValueTask.CompletedTask);
        var message = CreateMessage(new TestMessage("success"));
        Guid claimedToken = default;
        store.ClaimPendingAsync(Arg.Any<int>(), Arg.Do<Guid>(token => claimedToken = token), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                message.Claim(call.ArgAt<Guid>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(2).AddMinutes(1));
                return new[] { message };
            });
        var publisher = CreatePublisher(store, bus);

        var count = await publisher.PublishBatchAsync();

        Assert.Equal(1, count);
        await bus.Received(1).PublishAsync(Arg.Is<TestMessage>(value => value.Name == "success"), Arg.Any<DeliveryOptions?>());
        await store.Received(1).MarkPublishedAsync(message.MessageId, claimedToken, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(default, default, default!, default, default, default);
    }

    [Fact]
    public async Task PublishBatchAsync_WhenPublishFails_RecordsSanitizedFailure()
    {
        var store = Substitute.For<IOutboxStore>();
        var bus = Substitute.For<IMessageBus>();
        bus.PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions?>())
            .Returns(_ => ValueTask.FromException(new InvalidOperationException("transport\r\nfailed")));
        var message = CreateMessage(new TestMessage("failure"));
        store.ClaimPendingAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                message.Claim(call.ArgAt<Guid>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(2).AddMinutes(1));
                return new[] { message };
            });
        var publisher = CreatePublisher(store, bus);

        await publisher.PublishBatchAsync();

        await store.Received(1).MarkFailedAsync(
            message.MessageId,
            Arg.Any<Guid>(),
            "transport  failed",
            Arg.Any<DateTimeOffset>(),
            TimeSpan.FromSeconds(30),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkPublishedAsync(default, default, default, default);
    }

    private static OutboxPublisher CreatePublisher(IOutboxStore store, IMessageBus bus)
        => new(
            store,
            bus,
            Options.Create(new OutboxPublisherOptions()),
            TimeProvider.System);

    private static OutboxMessage CreateMessage(TestMessage payload)
        => OutboxMessage.Create(
            typeof(TestMessage).AssemblyQualifiedName!,
            System.Text.Json.JsonSerializer.Serialize(payload),
            DateTimeOffset.UtcNow);

    public sealed record TestMessage(string Name);
}
