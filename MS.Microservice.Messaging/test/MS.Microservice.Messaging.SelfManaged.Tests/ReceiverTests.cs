using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class ReceiverTests
{
    [Fact]
    public async Task RepeatedDeliveryCommitsBusinessEffectOnce_AndSeparateMessageStillExecutes()
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(Topology());
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BusinessContext>().Database.EnsureCreatedAsync();
        var receiver = provider.GetRequiredService<IMessageReceiver>();
        var message = Registry().Serialize(NewEvent());
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(message, "audit", default));
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(message, "audit", default));
        Assert.Equal(DeliveryResult.Acknowledge, await receiver.ReceiveAsync(Registry().Serialize(NewEvent()), "audit", default));
        await using var check = provider.CreateAsyncScope();
        Assert.Equal(2, await check.ServiceProvider.GetRequiredService<BusinessContext>().Set<BusinessRow>().CountAsync());
    }

    [Fact]
    public async Task BusyReceiptReturnsRequeueInsteadOfAcknowledging()
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(Topology(), options =>
        {
            options.BusyRecheckInterval = TimeSpan.FromMilliseconds(5);
            options.BusyWaitLimit = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var message = Registry().Serialize(NewEvent());
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BusinessContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<InboxStore<BusinessContext>>()
                .AcquireAsync(message, "audit", Guid.NewGuid(), default);
        }
        Assert.Equal(DeliveryResult.Requeue,
            await provider.GetRequiredService<IMessageReceiver>().ReceiveAsync(message, "audit", default));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("version")]
    [InlineData("type")]
    public async Task InvalidPayloadIsRecordedAsDeadLetter_WithoutBusinessEffects(string invalid)
    {
        await using var connection = await OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BusinessContext(connection));
        services.AddSelfManagedMessaging<BusinessContext>(Topology());
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BusinessContext>().Database.EnsureCreatedAsync();
        var encoded = Registry().Serialize(NewEvent());
        var message = invalid switch
        {
            "json" => encoded with { Payload = "{" },
            "version" => encoded with { ContractVersion = 99 },
            _ => encoded with { ContractName = "unregistered" }
        };
        Assert.Equal(DeliveryResult.Reject,
            await provider.GetRequiredService<IMessageReceiver>().ReceiveAsync(message, "audit", default));
        await using var check = provider.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<BusinessContext>();
        Assert.Empty(await context.Set<BusinessRow>().ToListAsync());
        Assert.Equal(InboxState.DeadLettered, (await context.Set<InboxEntry>().SingleAsync()).State);
    }

    [Fact]
    public void DuplicateProviderRegistrationAndInvalidDurationsAreRejected()
    {
        var services = new ServiceCollection();
        services.AddSelfManagedMessaging<BusinessContext>(Topology());
        Assert.Throws<InvalidOperationException>(() => services.AddSelfManagedMessaging<BusinessContext>(Topology()));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSelfManagedMessaging<BusinessContext>(Topology(),
            options => options.ProcessingTimeout = options.BrokerAcknowledgementTimeout));
    }

    internal static MessageTopology Topology() => new([MessageContract.For<Changed>("profile.changed")],
        [MessageSubscription.For<Changed, Handler>("audit")]);
    internal sealed class Handler(BusinessContext context) : IIntegrationEventHandler<Changed>
    {
        public Task HandleAsync(Changed message, MessageContext metadata, CancellationToken cancellationToken)
        {
            context.Add(new BusinessRow { Name = message.Name });
            return Task.CompletedTask;
        }
    }
}
