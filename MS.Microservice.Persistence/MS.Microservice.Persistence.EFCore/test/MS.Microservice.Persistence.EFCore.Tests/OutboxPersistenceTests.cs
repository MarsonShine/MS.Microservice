using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.Outbox;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class OutboxPersistenceTests
{
    [Fact]
    public async Task SaveEntitiesAsync_PersistsBusinessDataAndOutboxInOneSaveWithoutDirectDispatch()
    {
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        await using var context = CreateContext(dispatcher);
        var log = CreateLog();
        log.AddDomainEvent(new TestDomainEvent("created"));
        context.Logs.Add(log);

        var saved = await context.SaveEntitiesAsync();

        saved.Should().BeTrue();
        (await context.Logs.CountAsync()).Should().Be(1);
        var outbox = await context.OutboxMessages.SingleAsync();
        outbox.Status.Should().Be(OutboxMessageStatus.Pending);
        outbox.MessageType.Should().Contain(nameof(TestDomainEvent));
        outbox.Payload.Should().Contain("created");
        log.DomainEvents.Should().BeEmpty();
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task SaveEntitiesAsync_WhenDatabaseSaveFails_KeepsDomainEventsAndDetachesOutbox()
    {
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        await using var context = CreateContext(dispatcher, new ThrowingSaveChangesInterceptor());
        var log = CreateLog();
        var domainEvent = new TestDomainEvent("retry");
        log.AddDomainEvent(domainEvent);
        context.Logs.Add(log);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveEntitiesAsync());

        exception.Message.Should().Be("database save failed");
        log.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(domainEvent);
        context.ChangeTracker.Entries<OutboxMessage>().Should().BeEmpty();
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task ReplayDeadLetterAsync_OnlyResetsDeadLetteredMessage()
    {
        await using var context = CreateContext(Substitute.For<IDomainEventDispatcher>());
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create("event", "{}", now, maxRetryCount: 0);
        message.MarkFailed("permanent", now, TimeSpan.Zero);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore(context);

        var replayed = await store.ReplayDeadLetterAsync(message.MessageId, now.AddMinutes(1));

        replayed.Should().BeTrue();
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.RetryCount.Should().Be(0);
        message.NextAttemptAtUtc.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public async Task SaveEntitiesAsync_CapturesW3CTraceAndCorrelationContext()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "vendor=value";
        activity.AddBaggage("correlationId", "correlation-7");
        await using var context = CreateContext(Substitute.For<IDomainEventDispatcher>());
        var log = CreateLog();
        log.AddDomainEvent(new TestDomainEvent("traced"));
        context.Logs.Add(log);

        await context.SaveEntitiesAsync();

        var outbox = await context.OutboxMessages.SingleAsync();
        outbox.TraceParent.Should().Be(activity.Id);
        outbox.TraceState.Should().Be("vendor=value");
        outbox.TraceId.Should().Be(activity.TraceId.ToString());
        outbox.CorrelationId.Should().Be("correlation-7");
    }

    private static ActivationDbContext CreateContext(
        IDomainEventDispatcher dispatcher,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseInMemoryDatabase($"outbox-persistence-{Guid.NewGuid():N}");
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ActivationDbContext(
            builder.Options,
            Options.Create(new MsPlatformDbContextSettings()),
            dispatcher);
    }

    private static LogAggregateRoot CreateLog()
        => new(
            "event",
            "method",
            LogEventTypeEnum.Create,
            "description",
            "content",
            1,
            "127.0.0.1",
            "13800000000");

    private sealed record TestDomainEvent(string Name) : IDomainEvent;

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(
                new InvalidOperationException("database save failed"));
    }
}
