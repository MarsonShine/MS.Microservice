using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class ReplayTests
{
    [Fact]
    public async Task ListMergesFailureTimesAndPreservesSourceIdentityAtTheLimit()
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessContext>();
        await context.Database.EnsureCreatedAsync();
        var message = Registry().Serialize(NewEvent());
        var time = message.OccurredAtUtc.UtcDateTime;
        var outgoing = OutboxEntry.From(message, time);
        outgoing.State = OutboxState.DeadLettered;
        const string consumer = "audit:%消息";
        var incoming = new InboxEntry
        {
            MessageId = message.Id, ContractName = message.ContractName, ContractVersion = message.ContractVersion,
            Consumer = consumer, State = InboxState.DeadLettered, ReceivedAtUtc = time.AddHours(-1),
            CompletedAtUtc = time, ErrorCode = "handler"
        };
        context.AddRange(outgoing, incoming);
        await context.SaveChangesAsync();
        var failures = scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>();
        var expectedOut = new FailedMessage($"outbox:{message.Id:N}", message.Id, message.ContractName,
            message.ContractVersion, null, message.OccurredAtUtc, "unknown");
        var expectedIn = new FailedMessage($"inbox:{message.Id:N}:{Uri.EscapeDataString(consumer)}", message.Id,
            message.ContractName, message.ContractVersion, consumer, message.OccurredAtUtc, "handler");
        Assert.Equal(expectedOut, Assert.Single(await failures.ListAsync(1)));
        Assert.Equal(new[] { expectedOut, expectedIn }, await failures.ListAsync(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayAtomicallyRequeuesInbox_WithTheOriginalIdentity(bool existingPublication)
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        var message = Registry().Serialize(NewEvent());
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BusinessContext>();
            await context.Database.EnsureCreatedAsync();
            var store = scope.ServiceProvider.GetRequiredService<InboxStore<BusinessContext>>();
            var token = Guid.NewGuid();
            var claim = await store.AcquireAsync(message, "audit", token, default);
            await store.FailAsync(claim.Entry, token, "permanent", true, default);
            if (existingPublication)
            {
                var original = OutboxEntry.From(message, DateTime.UtcNow);
                original.State = OutboxState.Publishing;
                original.LockToken = Guid.NewGuid();
                original.LockedUntilUtc = DateTime.UtcNow.AddMinutes(1);
                context.Add(original);
                await context.SaveChangesAsync();
            }
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>();
            var failure = Assert.Single(await operations.ListAsync());
            Assert.Equal(message.Id, failure.MessageId);
            Assert.Equal(ReplayResult.Accepted, await operations.ReplayAsync(failure.FailureId));
            var context = scope.ServiceProvider.GetRequiredService<BusinessContext>();
            var replay = await context.Set<OutboxEntry>().AsNoTracking().SingleAsync();
            Assert.Equal(message, replay.ToMessage());
            Assert.Null(replay.LockToken);
            Assert.Equal(OutboxState.Pending, replay.State);
        }
        Assert.Equal(DeliveryResult.Acknowledge,
            await provider.GetRequiredService<IMessageReceiver>().ReceiveAsync(message, "audit", default));
        await using var check = provider.CreateAsyncScope();
        Assert.Equal(ReplayResult.InvalidState,
            await check.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ReplayAsync($"inbox:{message.Id:N}:audit"));
        Assert.Single(await check.ServiceProvider.GetRequiredService<BusinessContext>().Set<BusinessRow>().ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("outbox:invalid")]
    [InlineData("unsupported:00000000000000000000000000000001")]
    public async Task InvalidFailureIdsDoNotMutateStorage(string id)
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(ReceiverTests.Topology());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>();
        Assert.Equal(ReplayResult.NotFound, await operations.ReplayAsync(id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => operations.ListAsync(0));
    }
}
