using MS.Microservice.Messaging;
using Xunit;

namespace MS.Microservice.Messaging.Abstractions.Tests;

public sealed class MessageTopologyTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(8, 0)]
    [InlineData(8, 4)]
    [InlineData(8, 7)]
    public void FindsEachRegisteredConsumer(int count, int index)
    {
        var subscriptions = Enumerable.Range(0, count)
            .Select(i => MessageSubscription.For<Changed, Handler>($"consumer-{i}"))
            .ToArray();
        var topology = Topology(subscriptions);

        Assert.Same(subscriptions[index], topology.Subscription($"consumer-{index}"));
        Assert.Equal(subscriptions, topology.Subscriptions);
    }

    [Fact]
    public void KeepsOrdinalLookupForUnicodeCaseAndPrefixes()
    {
        var lower = MessageSubscription.For<Changed, Handler>("审计-a");
        var upper = MessageSubscription.For<Changed, Handler>("审计-A");
        var longer = MessageSubscription.For<Changed, Handler>("审计-a-extra");
        var topology = Topology([lower, upper, longer]);

        Assert.Same(lower, topology.Subscription("审计-a"));
        Assert.Same(upper, topology.Subscription("审计-A"));
        Assert.Same(longer, topology.Subscription("审计-a-extra"));
        Assert.Throws<MessageContractException>(() => topology.Subscription("审计"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownConsumersKeepContractException(string? consumer)
    {
        var topology = Topology([MessageSubscription.For<Changed, Handler>("known")]);
        Assert.Throws<MessageContractException>(() => topology.Subscription(consumer!));
    }

    [Fact]
    public void RejectsDuplicateConsumerButAcceptsCaseVariant()
    {
        var first = MessageSubscription.For<Changed, Handler>("audit");
        var duplicate = MessageSubscription.For<Changed, Handler>("audit");
        Assert.Throws<ArgumentException>(() => Topology([first, duplicate]));

        var variant = MessageSubscription.For<Changed, Handler>("Audit");
        var topology = Topology([first, variant]);
        Assert.Same(variant, topology.Subscription("Audit"));
    }

    [Fact]
    public void RegistrationIsSnapshotOfInput()
    {
        var first = MessageSubscription.For<Changed, Handler>("first");
        var subscriptions = new List<MessageSubscription> { first };
        var topology = Topology(subscriptions);
        subscriptions[0] = MessageSubscription.For<Changed, Handler>("replacement");

        Assert.Same(first, topology.Subscription("first"));
        Assert.Throws<MessageContractException>(() => topology.Subscription("replacement"));
    }

    [Fact]
    public void EmptyTopologyHasNoConsumer()
        => Assert.Throws<MessageContractException>(() => Topology([]).Subscription("any"));

    [Fact]
    public void ConsumerNameLengthBoundaryIsUnchanged()
    {
        var validName = new string('x', 200);
        var topology = Topology([MessageSubscription.For<Changed, Handler>(validName)]);
        Assert.Equal(validName, topology.Subscription(validName).Consumer);
        Assert.Throws<ArgumentException>(() => Topology([
            MessageSubscription.For<Changed, Handler>(new string('x', 201))]));
    }

    private static MessageTopology Topology(IEnumerable<MessageSubscription> subscriptions)
        => new([MessageContract.For<Changed>("changed")], subscriptions);

    private sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
    private sealed class Handler : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
