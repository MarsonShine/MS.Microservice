using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.Inbox;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class InboxStoreTests
{
    [Fact]
    public async Task TryRegisterAsync_FirstDelivery_CreatesReceipt()
    {
        await using var context = CreateContext();
        var store = new EfCoreInboxStore(context);
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var registration = await store.TryRegisterAsync(messageId, "Billing", now, "OrderCreated");

        registration.IsFirstDelivery.Should().BeTrue();
        registration.Receipt.Status.Should().Be(InboxMessageStatus.Received);
        registration.Receipt.DeduplicationKey.Should().Be(InboxMessage.BuildDeduplicationKey(messageId, "Billing"));
        (await context.InboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TryRegisterAsync_DuplicateDelivery_ReusesReceiptAndRecordsDuplicate()
    {
        await using var context = CreateContext();
        var store = new EfCoreInboxStore(context);
        var messageId = Guid.NewGuid();
        var firstAt = DateTimeOffset.UtcNow;
        await store.TryRegisterAsync(messageId, "Billing", firstAt);

        var duplicate = await store.TryRegisterAsync(messageId, "Billing", firstAt.AddSeconds(5));

        duplicate.IsFirstDelivery.Should().BeFalse();
        duplicate.Receipt.DuplicateCount.Should().Be(1);
        duplicate.Receipt.LastDuplicateAtUtc.Should().Be(firstAt.AddSeconds(5));
        (await context.InboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TryRegisterAsync_SameMessageDifferentConsumers_CreatesSeparateReceipts()
    {
        await using var context = CreateContext();
        var store = new EfCoreInboxStore(context);
        var messageId = Guid.NewGuid();

        var billing = await store.TryRegisterAsync(messageId, "Billing", DateTimeOffset.UtcNow);
        var inventory = await store.TryRegisterAsync(messageId, "Inventory", DateTimeOffset.UtcNow);

        billing.IsFirstDelivery.Should().BeTrue();
        inventory.IsFirstDelivery.Should().BeTrue();
        (await context.InboxMessages.CountAsync()).Should().Be(2);
    }

    private static ActivationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseInMemoryDatabase($"inbox-store-{Guid.NewGuid():N}")
            .Options;
        return new ActivationDbContext(
            options,
            Options.Create(new MsPlatformDbContextSettings()),
            Substitute.For<IDomainEventDispatcher>());
    }
}
