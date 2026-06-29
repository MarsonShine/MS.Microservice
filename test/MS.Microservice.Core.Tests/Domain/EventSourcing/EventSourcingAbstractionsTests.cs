using System;
using FluentAssertions;
using MS.Microservice.Domain.EventSourcing;
using Xunit;

namespace MS.Microservice.Core.Tests.Domain.EventSourcing;

public sealed class EventSourcingAbstractionsTests
{
    [Fact]
    public void Records_ShouldStoreAssignedValues()
    {
        var metadata = new EventMetadata("corr", "cause", "user", "tenant", "trace", 2);
        var createdAt = DateTimeOffset.UtcNow;
        var envelope = new EventEnvelope<SampleEvent>(
            Guid.NewGuid(),
            "stream-1",
            "order",
            3,
            10,
            new SampleEvent("created"),
            metadata,
            createdAt);
        var snapshot = new AggregateSnapshot<int>("stream-1", "order", 4, 42, createdAt);

        envelope.StreamId.Should().Be("stream-1");
        envelope.Data.Name.Should().Be("created");
        envelope.Metadata.Should().Be(metadata);
        snapshot.State.Should().Be(42);
        snapshot.Version.Should().Be(4);
    }

    [Fact]
    public void EventStoreConcurrencyException_ShouldExposeProperties()
    {
        var inner = new InvalidOperationException("boom");
        var exception = new EventStoreConcurrencyException("order-1", 2, 3, inner);

        exception.StreamId.Should().Be("order-1");
        exception.ExpectedVersion.Should().Be(2);
        exception.ActualVersion.Should().Be(3);
        exception.InnerException.Should().BeSameAs(inner);
        exception.Message.Should().Contain("order-1");
        exception.Message.Should().Contain("期望版本 2");
    }

    private sealed record SampleEvent(string Name) : IEventSourcedEvent;
}
