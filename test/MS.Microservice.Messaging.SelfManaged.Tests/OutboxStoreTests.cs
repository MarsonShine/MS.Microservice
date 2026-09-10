using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class OutboxStoreTests
{
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
        var entry = Assert.Single(await store.ClaimAsync(oldToken, default));
        clock.Advance(options.PublishingLease);
        var newToken = Guid.NewGuid();
        Assert.Single(await store.ClaimAsync(newToken, default));
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
        var entry = Assert.Single(await store.ClaimAsync(token, default));
        Assert.Empty(await store.ClaimAsync(Guid.NewGuid(), default));
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
        var first = Assert.Single(await store.ClaimAsync(token, default));
        Assert.True(await store.FailAsync(first, token, "transient", false, default));
        Assert.Empty(await store.ClaimAsync(token, default));
        clock.Advance(options.InitialRetryDelay);
        var second = Assert.Single(await store.ClaimAsync(token, default));
        Assert.Equal(1, second.AttemptCount);
        Assert.True(await store.FailAsync(second, token, "transient", false, default));
        clock.Advance(TimeSpan.FromDays(60));
        Assert.Equal(0, await store.CleanupAsync(default));
        Assert.Equal(ReplayResult.Accepted, await store.ReplayAsync(message.Id, default));
        var replay = Assert.Single(await store.ClaimAsync(token, default));
        Assert.Equal(message, replay.ToMessage());
        Assert.Equal(0, replay.AttemptCount);
        Assert.True(await store.CompleteAsync(message.Id, token, default));
        Assert.Equal(ReplayResult.InvalidState, await store.ReplayAsync(message.Id, default));
        clock.Advance(options.PublishedRetention + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await store.CleanupAsync(default));
        Assert.Equal(ReplayResult.NotFound, await store.ReplayAsync(message.Id, default));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(100, 900)]
    public void BackoffIsBounded(int failureCount, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), new SelfManagedOptions().RetryDelay(failureCount));

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
