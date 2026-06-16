using FluentAssertions;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Events;

namespace MS.Microservice.Core.Tests.Domain;

public sealed class DomainEventsOutboxInboxTests
{
    [Fact]
    public void Entity_ShouldExposeDomainEventsThroughIHasDomainEvents()
    {
        var entity = new TestEntity(1);
        var domainEvent = new TestDomainEvent("created");

        entity.AddDomainEvent(domainEvent);

        var hasEvents = (IHasDomainEvents)entity;
        hasEvents.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(domainEvent);

        hasEvents.ClearDomainEvents();
        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void OutboxMessage_ShouldMoveThroughRetryAndDeadLetterStates()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create("OrderCreated", "{}", now, maxRetryCount: 1);

        message.MarkPublishing(now.AddSeconds(1));
        message.Status.Should().Be(OutboxMessageStatus.Publishing);

        message.MarkFailed("network", now.AddSeconds(2), TimeSpan.FromMinutes(5));
        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.RetryCount.Should().Be(1);
        message.NextAttemptAtUtc.Should().Be(now.AddSeconds(2).AddMinutes(5));

        message.MarkFailed("still down", now.AddSeconds(3), TimeSpan.FromMinutes(5));
        message.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        message.NextAttemptAtUtc.Should().BeNull();
    }

    [Fact]
    public void OutboxMessage_ShouldMarkPublishedAndClearRetryMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create("OrderCreated", "{}", now);
        message.MarkFailed("temporary", now, TimeSpan.FromSeconds(10));

        message.MarkPublished(now.AddSeconds(1));

        message.Status.Should().Be(OutboxMessageStatus.Published);
        message.PublishedAtUtc.Should().Be(now.AddSeconds(1));
        message.LastError.Should().BeNull();
        message.NextAttemptAtUtc.Should().BeNull();
    }

    [Fact]
    public void InboxMessage_ShouldExposeStableDeduplicationKeyAndDuplicateMetadata()
    {
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var inbox = InboxMessage.Create(messageId, "Billing", now);

        inbox.DeduplicationKey.Should().Be(InboxMessage.BuildDeduplicationKey(messageId, "Billing"));
        inbox.Matches(messageId, "Billing").Should().BeTrue();

        inbox.RecordDuplicate(now.AddSeconds(5));
        inbox.DuplicateCount.Should().Be(1);
        inbox.LastDuplicateAtUtc.Should().Be(now.AddSeconds(5));
    }

    [Fact]
    public void InboxMessage_ShouldMoveThroughProcessingStates()
    {
        var now = DateTimeOffset.UtcNow;
        var inbox = InboxMessage.Create(Guid.NewGuid(), "Inventory", now);

        inbox.MarkProcessing(now.AddSeconds(1));
        inbox.Status.Should().Be(InboxMessageStatus.Processing);

        inbox.MarkFailed("temporary");
        inbox.Status.Should().Be(InboxMessageStatus.Failed);

        inbox.MarkProcessed(now.AddSeconds(2));
        inbox.Status.Should().Be(InboxMessageStatus.Processed);
        inbox.LastError.Should().BeNull();
    }

    private sealed record TestDomainEvent(string Name) : IDomainEvent;

    private sealed class TestEntity : Entity<int>
    {
        public TestEntity(int id)
        {
            Id = id;
        }
    }
}
