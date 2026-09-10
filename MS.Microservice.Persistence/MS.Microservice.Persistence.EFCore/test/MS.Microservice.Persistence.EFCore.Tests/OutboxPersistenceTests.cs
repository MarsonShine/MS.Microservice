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
    [Theory]
    [InlineData("created")]
    [InlineData("中文")]
    [InlineData("")]
    [InlineData("retry")]
    public async Task LegacySavePreservesEventsWithoutCreatingAnotherOutbox(string name)
    {
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        await using var context = CreateContext(dispatcher);
        var log = CreateLog();
        log.AddDomainEvent(new TestDomainEvent(name));
        context.Logs.Add(log);
        await context.SaveEntitiesAsync();
        Assert.Equal(1, await context.Logs.CountAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
        Assert.Single(log.DomainEvents);
        await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task FailedLegacySavePreservesEventsAndDoesNotStageMessages()
    {
        await using var context = CreateContext(Substitute.For<IDomainEventDispatcher>(), new ThrowingSaveChangesInterceptor());
        var log = CreateLog();
        log.AddDomainEvent(new TestDomainEvent("failure"));
        context.Logs.Add(log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveEntitiesAsync());
        Assert.Single(log.DomainEvents);
        Assert.Empty(context.ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public async Task LegacySaveLeavesHistoricalPendingAndDeadLettersUntouched()
    {
        await using var context = CreateContext(Substitute.For<IDomainEventDispatcher>());
        var pending = OutboxMessage.Create("historical", "{}", DateTimeOffset.UtcNow);
        var dead = OutboxMessage.Create("historical", "{}", DateTimeOffset.UtcNow, maxRetryCount: 0);
        dead.MarkFailed("permanent", DateTimeOffset.UtcNow, TimeSpan.Zero);
        context.OutboxMessages.AddRange(pending, dead);
        await context.SaveChangesAsync();
        context.Logs.Add(CreateLog());
        await context.SaveEntitiesAsync();
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        Assert.Equal(OutboxMessageStatus.Pending, pending.Status);
        Assert.Equal(OutboxMessageStatus.DeadLettered, dead.Status);
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
