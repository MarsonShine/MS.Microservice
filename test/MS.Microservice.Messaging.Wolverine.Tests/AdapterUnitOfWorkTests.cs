using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using global::Wolverine;
using global::Wolverine.EntityFrameworkCore;
using Xunit;

namespace MS.Microservice.Messaging.Wolverine.Tests;

public sealed class AdapterUnitOfWorkTests
{
    [Fact]
    public void IdentityRuleUsesLogicalEventIdAndRegisteredAlias()
    {
        var message = Event();
        var envelope = new Envelope(message);
        new IntegrationEventIdentityRule(Registry()).Modify(envelope);
        Assert.Equal(message.Id, envelope.Id);
        Assert.Equal("profile.changed.v1", envelope.MessageType);
        Assert.Equal("1", envelope.Headers["ms-contract-version"]);
    }

    [Fact]
    public async Task FailedOperationNeverHandsPendingEventsToNativeOutbox()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var native = Substitute.For<IDbContextOutbox<BusinessContext>>();
        var unit = new WolverineUnitOfWork<BusinessContext>(context, () => native, Registry());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
        {
            context.Add(new Row { Id = 1 });
            await context.SaveChangesAsync(token);
            await unit.EnqueueAsync(Event(), token);
            throw new InvalidOperationException("fail");
        }));
        Assert.Empty(await context.Set<Row>().ToListAsync());
        await native.DidNotReceive().PublishAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<DeliveryOptions?>());
        await native.DidNotReceive().SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessUsesNativeSaveAndCommit_AndAFreshOutboxForEachOperation()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var created = new List<IDbContextOutbox<BusinessContext>>();
        var unit = new WolverineUnitOfWork<BusinessContext>(context, () =>
        {
            var native = Substitute.For<IDbContextOutbox<BusinessContext>>();
            native.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(async call =>
            {
                await context.SaveChangesAsync(call.Arg<CancellationToken>());
                await context.Database.CommitTransactionAsync(call.Arg<CancellationToken>());
            });
            created.Add(native);
            return native;
        }, Registry());
        for (var id = 1; id <= 2; id++)
        {
            await unit.ExecuteAsync(async token =>
            {
                context.Add(new Row { Id = id });
                await unit.EnqueueAsync(Event(), token);
            });
        }
        Assert.Equal(2, created.Count);
        Assert.Equal(2, await context.Set<Row>().CountAsync());
        foreach (var outbox in created) await outbox.Received(1).SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncomingBridgeRequiresNativeTransaction_AndNeverCreatesAnotherOutbox()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        var unit = new WolverineUnitOfWork<BusinessContext>(context, () => throw new InvalidOperationException("must not create"), Registry());
        var incoming = Substitute.For<IMessageContext>();
        Assert.Throws<InvalidOperationException>(() => unit.BeginIncoming(incoming));
        await using var transaction = await context.Database.BeginTransactionAsync();
        unit.BeginIncoming(incoming);
        var message = Event();
        await unit.EnqueueAsync(message);
        await unit.FlushIncomingAsync(default);
        unit.EndIncoming();
        await incoming.Received(1).PublishAsync(Arg.Is<IIntegrationEvent>(x => x.Id == message.Id), Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task CancellationBeforeCommitDoesNotPublishOrPersistBusinessChanges()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var native = Substitute.For<IDbContextOutbox<BusinessContext>>();
        var unit = new WolverineUnitOfWork<BusinessContext>(context, () => native, Registry());
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(async token =>
        {
            context.Add(new Row { Id = 1 });
            await context.SaveChangesAsync(token);
            await unit.EnqueueAsync(Event(), token);
            cancellation.Cancel();
        }, cancellation.Token));
        Assert.Empty(await context.Set<Row>().ToListAsync());
        await native.DidNotReceive().PublishAsync(Arg.Any<IIntegrationEvent>(), Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task CaughtNestedFailureMakesTheNativeTransactionRollbackOnly()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var native = Substitute.For<IDbContextOutbox<BusinessContext>>();
        var unit = new WolverineUnitOfWork<BusinessContext>(context, () => native, Registry());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
        {
            try { await unit.ExecuteAsync<int>(_ => throw new InvalidOperationException()); }
            catch (InvalidOperationException) { }
            await unit.EnqueueAsync(Event(), token);
        }));
        await native.DidNotReceive().SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
    }

    private static MessageContractRegistry Registry() => new([MessageContract.For<Changed>("profile.changed")]);
    private static Changed Event() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "value");
    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }
    public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc, string Name) : IIntegrationEvent;
    public sealed class Row { public int Id { get; set; } }
    public sealed class BusinessContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>();
    }
}
