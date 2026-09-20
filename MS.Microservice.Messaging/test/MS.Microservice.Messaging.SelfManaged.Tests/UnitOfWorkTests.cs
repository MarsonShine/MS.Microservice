using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class UnitOfWorkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusinessAndMessageCommitTogether_InDifferentBusinessContexts(bool alternative)
    {
        await using var connection = await OpenAsync();
        await using DbContext context = alternative ? new OtherContext(connection) : new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var unit = Create(context);
        var message = NewEvent();
        await unit.ExecuteAsync(async token =>
        {
            context.Add(new BusinessRow { Id = 1, Name = "中文" });
            await unit.EnqueueAsync(message, token);
            await unit.EnqueueAsync(message, token);
            return true;
        });
        Assert.Equal("中文", (await context.Set<BusinessRow>().SingleAsync()).Name);
        var saved = await context.Set<OutboxEntry>().SingleAsync();
        Assert.Equal(message.Id, saved.Id);
        Assert.Equal(message, Registry().Deserialize(saved.ToMessage()));
    }

    [Fact]
    public async Task FailedOperationRollsBack_AndDoesNotLeakMessagesIntoNextOperation()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var unit = Create(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
        {
            context.Add(new BusinessRow { Id = 1, Name = "rolled back" });
            await unit.EnqueueAsync(NewEvent(), token);
            await context.SaveChangesAsync(token);
            throw new InvalidOperationException("business failure");
        }));
        Assert.Empty(await context.Set<BusinessRow>().ToListAsync());
        Assert.Empty(await context.Set<OutboxEntry>().ToListAsync());
        await unit.ExecuteAsync(_ => { context.Add(new BusinessRow { Id = 2, Name = "next" }); return Task.CompletedTask; });
        Assert.Single(await context.Set<BusinessRow>().ToListAsync());
        Assert.Empty(await context.Set<OutboxEntry>().ToListAsync());
    }

    [Fact]
    public async Task CaughtNestedFailureStillPreventsOuterCommit()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var unit = Create(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
        {
            context.Add(new BusinessRow { Id = 1 });
            try { await unit.ExecuteAsync<int>(_ => throw new InvalidOperationException()); }
            catch (InvalidOperationException) { }
            await unit.EnqueueAsync(NewEvent(), token);
        }));
        Assert.Empty(await context.Set<BusinessRow>().ToListAsync());
        Assert.Empty(await context.Set<OutboxEntry>().ToListAsync());
    }

    [Fact]
    public async Task CancellationRollsBack_AndEnqueueOutsideTransactionIsRejected()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var unit = Create(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.EnqueueAsync(NewEvent()).AsTask());
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(async token =>
        {
            context.Add(new BusinessRow { Id = 1 });
            await unit.EnqueueAsync(NewEvent(), token);
            cancellation.Cancel();
        }, cancellation.Token));
        Assert.Empty(await context.Set<OutboxEntry>().ToListAsync());
        Assert.Empty(await context.Set<BusinessRow>().ToListAsync());
    }

    [Fact]
    public async Task ConflictingPayloadForSameIdFailsAtomically()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var unit = Create(context);
        var message = NewEvent();
        await Assert.ThrowsAsync<MessageContractException>(() => unit.ExecuteAsync(async token =>
        {
            await unit.EnqueueAsync(message, token);
            await unit.EnqueueAsync(message with { Name = "different" }, token);
        }));
        Assert.Empty(await context.Set<OutboxEntry>().ToListAsync());
    }

    internal static MessageContractRegistry Registry() => new([MessageContract.For<Changed>("profile.changed", TestMessageJsonContext.Default.Changed)]);
    internal static Changed NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "created");
    private static SelfManagedUnitOfWork<DbContext> Create(DbContext context) => new(context, Registry(), TimeProvider.System);
    internal static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    public sealed record Changed(Guid Id, DateTimeOffset OccurredAtUtc, string Name) : IIntegrationEvent;
    internal sealed class BusinessRow { public int Id { get; set; } public string Name { get; set; } = ""; }
    internal sealed class BusinessContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<BusinessRow>(); model.AddSelfManagedMessaging(); }
    }
    internal sealed class OtherContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<BusinessRow>().ToTable("OtherBusiness"); model.AddSelfManagedMessaging("other_messages"); }
    }
}
