using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class ReplayTests
{
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
