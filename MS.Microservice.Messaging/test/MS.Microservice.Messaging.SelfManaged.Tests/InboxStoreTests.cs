using Microsoft.EntityFrameworkCore;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class InboxStoreTests
{
    [Fact]
    public async Task BusyReceiptIsNotCompleted_AndCrashLeaseCanBeReclaimed()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new OutboxStoreTests.Clock();
        var options = new SelfManagedOptions();
        var store = new InboxStore<BusinessContext>(context, options, clock);
        var message = Registry().Serialize(NewEvent());
        var oldToken = Guid.NewGuid();
        Assert.Equal(InboxAcquisition.Acquired, (await store.AcquireAsync(message, "audit", oldToken, default)).Result);
        Assert.Equal(InboxAcquisition.Busy, (await store.AcquireAsync(message, "audit", Guid.NewGuid(), default)).Result);
        Assert.Equal(InboxAcquisition.Acquired, (await store.AcquireAsync(message, "other-consumer", Guid.NewGuid(), default)).Result);
        clock.Advance(options.ProcessingLease);
        var token = Guid.NewGuid();
        Assert.Equal(InboxAcquisition.Acquired, (await store.AcquireAsync(message, "audit", token, default)).Result);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.False(await store.CompleteAsync(message.Id, "audit", oldToken, default));
            Assert.True(await store.CompleteAsync(message.Id, "audit", token, default));
            await transaction.CommitAsync();
        }
        Assert.Equal(InboxAcquisition.AlreadyProcessed, (await store.AcquireAsync(message, "audit", Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task CompletionRollsBackWithBusinessChanges()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var store = new InboxStore<BusinessContext>(context, new(), TimeProvider.System);
        var message = Registry().Serialize(NewEvent());
        var token = Guid.NewGuid();
        await store.AcquireAsync(message, "audit", token, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(message.Id, "audit", token, default));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Add(new BusinessRow { Id = 1 });
            await context.SaveChangesAsync();
            Assert.True(await store.CompleteAsync(message.Id, "audit", token, default));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.Empty(await context.Set<BusinessRow>().ToListAsync());
        Assert.Equal(InboxState.Processing, (await store.FindAsync(message.Id, "audit", default))!.State);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("renew")]
    [InlineData("complete")]
    public async Task ExpiredTokenCannotMutateReceipt(string operation)
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var options = new SelfManagedOptions();
        var clock = new OutboxStoreTests.Clock();
        var store = new InboxStore<BusinessContext>(context, options, clock);
        var message = Registry().Serialize(NewEvent());
        var token = Guid.NewGuid();
        var claim = await store.AcquireAsync(message, "audit", token, default);
        clock.Advance(options.ProcessingLease);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var changed = operation switch
        {
            "fail" => await store.FailAsync(claim.Entry, token, "failure", false, default),
            "renew" => await store.RenewAsync(message.Id, "audit", token, default),
            _ => await store.CompleteAsync(message.Id, "audit", token, default)
        };
        Assert.False(changed);
    }

    [Fact]
    public async Task PermanentFailureIsRetained_AndCannotMasqueradeAsProcessed()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var clock = new OutboxStoreTests.Clock();
        var store = new InboxStore<BusinessContext>(context, new(), clock);
        var message = Registry().Serialize(NewEvent());
        var token = Guid.NewGuid();
        var claim = await store.AcquireAsync(message, "audit", token, default);
        Assert.True(await store.FailAsync(claim.Entry, token, "invalid-contract", true, default));
        clock.Advance(TimeSpan.FromDays(90));
        Assert.Equal(0, await store.CleanupAsync(default));
        Assert.Equal(InboxAcquisition.DeadLettered, (await store.AcquireAsync(message, "audit", Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task ReusedIdWithDifferentContentDoesNotOverwriteExistingReceipt()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var store = new InboxStore<BusinessContext>(context, new(), TimeProvider.System);
        var message = Registry().Serialize(NewEvent());
        await store.AcquireAsync(message, "audit", Guid.NewGuid(), default);
        await Assert.ThrowsAsync<MessageContractException>(() => store.AcquireAsync(
            message with { Payload = "{}" }, "audit", Guid.NewGuid(), default));
        Assert.Equal(message.Payload, (await store.FindAsync(message.Id, "audit", default))!.Payload);
    }

    [Theory]
    [InlineData("correlation", 201)]
    [InlineData("traceparent", 129)]
    [InlineData("tracestate", 513)]
    public async Task InvalidMetadataCannotReachInboxInsert(string field, int length)
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var store = new InboxStore<BusinessContext>(context, new(), TimeProvider.System);
        var original = Registry().Serialize(NewEvent());
        var value = new string('x', length);
        var invalid = field switch
        {
            "correlation" => original with { CorrelationId = value },
            "traceparent" => original with { TraceParent = value },
            _ => original with { TraceState = value }
        };
        await Assert.ThrowsAsync<MessageContractException>(() =>
            store.AcquireAsync(invalid, "audit", Guid.NewGuid(), default));
        Assert.Empty(await context.Set<InboxEntry>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundaryMetadataIsStoredWithoutChangingItsValue(bool unicodeCorrelation)
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        await context.Database.EnsureCreatedAsync();
        var store = new InboxStore<BusinessContext>(context, new(), TimeProvider.System);
        var message = Registry().Serialize(NewEvent()) with
        {
            CorrelationId = unicodeCorrelation ? new string('中', 85) : new string('a', 200),
            TraceParent = new string('p', 128), TraceState = new string('中', 512)
        };
        Assert.Equal(InboxAcquisition.Acquired,
            (await store.AcquireAsync(message, "audit", Guid.NewGuid(), default)).Result);
        var saved = await store.FindAsync(message.Id, "audit", default);
        Assert.Equal(message.CorrelationId, saved!.CorrelationId);
        Assert.Equal(message.TraceParent, saved.TraceParent);
        Assert.Equal(message.TraceState, saved.TraceState);
    }
}
