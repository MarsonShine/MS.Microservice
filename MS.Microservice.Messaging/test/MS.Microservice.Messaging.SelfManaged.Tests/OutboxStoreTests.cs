using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class OutboxStoreTests
{
    [Fact]
    public async Task CandidateReadDelayDoesNotConsumeTheNewLease()
    {
        await using var connection = await OpenAsync();
        var clock = new Clock();
        var options = new SelfManagedOptions { PublishingLease = TimeSpan.FromSeconds(1) };
        var delay = new AdvanceClockAfterRead(clock, TimeSpan.FromSeconds(5));
        var dbOptions = new DbContextOptionsBuilder<ClaimContext>().UseSqlite(connection).AddInterceptors(delay).Options;
        await using var context = new ClaimContext(dbOptions);
        await context.Database.EnsureCreatedAsync();
        context.Add(OutboxEntry.From(Registry().Serialize(NewEvent()), clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var store = new OutboxStore<ClaimContext>(context, options, clock);
        var token = Guid.NewGuid();
        delay.Armed = true;

        var entry = Assert.Single(await store.ClaimAsync(token, default).ToListAsync());

        Assert.False(delay.Armed);
        Assert.Equal(clock.GetUtcNow().UtcDateTime + options.PublishingLease, entry.LockedUntilUtc);
        Assert.True(await store.RenewAsync(entry.Id, token, default));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("renew")]
    public async Task OldOwnerCannotChangeReclaimedMessage(string operation)
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new Clock();
        var options = new SelfManagedOptions();
        var store = new OutboxStore<BusinessContext>(context, options, clock);
        context.Add(OutboxEntry.From(Registry().Serialize(NewEvent()), clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var oldToken = Guid.NewGuid();
        var entry = Assert.Single(await store.ClaimAsync(oldToken, default).ToListAsync());
        clock.Advance(options.PublishingLease);
        var newToken = Guid.NewGuid();
        Assert.Single(await store.ClaimAsync(newToken, default).ToListAsync());
        var changed = operation switch
        {
            "complete" => await store.CompleteAsync(entry.Id, oldToken, default),
            "fail" => await store.FailAsync(entry, oldToken, "failure", false, default),
            _ => await store.RenewAsync(entry.Id, oldToken, default)
        };
        Assert.False(changed);
        Assert.True(await store.CompleteAsync(entry.Id, newToken, default));
    }

    [Fact]
    public async Task ActiveLeasePreventsSecondClaim_AndExpiredLeaseCannotConfirm()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new Clock();
        var options = new SelfManagedOptions();
        var store = new OutboxStore<BusinessContext>(context, options, clock);
        context.Add(OutboxEntry.From(Registry().Serialize(NewEvent()), clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var token = Guid.NewGuid();
        var entry = Assert.Single(await store.ClaimAsync(token, default).ToListAsync());
        Assert.Empty(await store.ClaimAsync(Guid.NewGuid(), default).ToListAsync());
        clock.Advance(options.PublishingLease);
        Assert.False(await store.CompleteAsync(entry.Id, token, default));
    }

    [Fact]
    public async Task RetryIsDelayed_DeadLettersAreRetained_AndReplayPreservesIdentity()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new Clock();
        var options = new SelfManagedOptions { MaxRetryAttempts = 1 };
        var store = new OutboxStore<BusinessContext>(context, options, clock);
        var message = Registry().Serialize(NewEvent());
        context.Add(OutboxEntry.From(message, clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var token = Guid.NewGuid();
        var first = Assert.Single(await store.ClaimAsync(token, default).ToListAsync());
        Assert.True(await store.FailAsync(first, token, "transient", false, default));
        Assert.Empty(await store.ClaimAsync(token, default).ToListAsync());
        clock.Advance(options.InitialRetryDelay);
        var second = Assert.Single(await store.ClaimAsync(token, default).ToListAsync());
        Assert.Equal(1, second.AttemptCount);
        Assert.True(await store.FailAsync(second, token, "transient", false, default));
        clock.Advance(TimeSpan.FromDays(60));
        Assert.Equal(0, await store.CleanupAsync(default));
        Assert.Equal(ReplayResult.Accepted, await store.ReplayAsync(message.Id, default));
        var replay = Assert.Single(await store.ClaimAsync(token, default).ToListAsync());
        Assert.Equal(message, replay.ToMessage());
        Assert.Equal(0, replay.AttemptCount);
        Assert.True(await store.CompleteAsync(message.Id, token, default));
        Assert.Equal(ReplayResult.InvalidState, await store.ReplayAsync(message.Id, default));
        clock.Advance(options.PublishedRetention + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await store.CleanupAsync(default));
        Assert.Equal(ReplayResult.NotFound, await store.ReplayAsync(message.Id, default));
    }

    [Fact]
    public async Task StoppingEnumerationLeavesRemainingCandidatesUnclaimed()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new Clock();
        for (var i = 0; i < 3; i++)
            context.Add(OutboxEntry.From(Registry().Serialize(NewEvent()), clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var store = new OutboxStore<BusinessContext>(context, new SelfManagedOptions(), clock);

        await using (var claims = store.ClaimAsync(Guid.NewGuid(), default).GetAsyncEnumerator())
            Assert.True(await claims.MoveNextAsync());

        var entries = await context.Set<OutboxEntry>().AsNoTracking().ToListAsync();
        Assert.Single(entries, entry => entry.State == OutboxState.Publishing);
        var pending = entries.Where(entry => entry.State == OutboxState.Pending).ToArray();
        Assert.Equal(2, pending.Length);
        Assert.All(pending, entry =>
        {
            Assert.Null(entry.LockToken);
            Assert.Null(entry.LockedUntilUtc);
        });
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(100, 900)]
    public void BackoffIsBounded(int failureCount, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), new SelfManagedOptions().RetryDelay(failureCount));

    private sealed class ClaimContext(DbContextOptions<ClaimContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.AddSelfManagedMessaging();
    }

    private sealed class AdvanceClockAfterRead(Clock clock, TimeSpan elapsed) : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                clock.Advance(elapsed);
            }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
