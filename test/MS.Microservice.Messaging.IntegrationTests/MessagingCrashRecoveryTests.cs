using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Persistence.EFCore.Inbox;
using MS.Microservice.Persistence.EFCore.Outbox;
using NSubstitute;
using Wolverine;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class MessagingCrashRecoveryTests(MessagingRecoveryFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [MessagingIntegrationFact]
    public async Task SaveThenCrash_PendingOutboxSurvivesNewDbContext()
    {
        await using (var savingContext = fixture.CreateDbContext())
        {
            var log = CreateLog();
            log.AddDomainEvent(new RecoveryDomainEvent("persisted"));
            savingContext.Logs.Add(log);
            await savingContext.SaveEntitiesAsync();
        }

        await using var restartedContext = fixture.CreateDbContext();
        (await restartedContext.Logs.CountAsync()).Should().Be(1);
        var outbox = await restartedContext.OutboxMessages.SingleAsync();
        outbox.Status.Should().Be(OutboxMessageStatus.Pending);
        outbox.Payload.Should().Contain("persisted");
    }

    [MessagingIntegrationFact]
    public async Task PublisherRestart_AfterExpiredLease_ReclaimsUnconfirmedMessage()
    {
        var now = DateTimeOffset.UtcNow;
        var message = CreateOutboxMessage(new RecoveryIntegrationEvent("lease"), now);
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.OutboxMessages.Add(message);
            await setupContext.SaveChangesAsync();
        }

        var firstToken = Guid.NewGuid();
        await using (var firstProcessContext = fixture.CreateDbContext())
        {
            var firstStore = new EfCoreOutboxStore(firstProcessContext);
            var firstClaim = await firstStore.ClaimPendingAsync(1, firstToken, now, TimeSpan.FromSeconds(1));
            firstClaim.Should().ContainSingle();
        }

        await using var restartedContext = fixture.CreateDbContext();
        var restartedStore = new EfCoreOutboxStore(restartedContext);
        var secondToken = Guid.NewGuid();
        var reclaimed = await restartedStore.ClaimPendingAsync(
            1,
            secondToken,
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1));

        reclaimed.Should().ContainSingle();
        reclaimed[0].LockToken.Should().Be(secondToken);
    }

    [MessagingIntegrationFact]
    public async Task ConcurrentDuplicateDelivery_RegistersOneInboxReceipt()
    {
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstStore = new EfCoreInboxStore(firstContext);
        var secondStore = new EfCoreInboxStore(secondContext);

        var results = await Task.WhenAll(
            firstStore.TryRegisterAsync(messageId, "Billing", now),
            secondStore.TryRegisterAsync(messageId, "Billing", now.AddMilliseconds(1)));

        results.Count(result => result.IsFirstDelivery).Should().Be(1);
        await using var verificationContext = fixture.CreateDbContext();
        var receipt = await verificationContext.InboxMessages.SingleAsync();
        receipt.DuplicateCount.Should().Be(1);
    }

    [MessagingIntegrationFact]
    public async Task OutboxPublisher_UsesRabbitMqAndConfirmsPublishedMessage()
    {
        var now = DateTimeOffset.UtcNow;
        var message = CreateOutboxMessage(new RecoveryIntegrationEvent("rabbit"), now);
        await using var context = fixture.CreateDbContext();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore(context);
        var publisher = new OutboxPublisher(
            store,
            fixture.MessageBus,
            Options.Create(new OutboxPublisherOptions()),
            TimeProvider.System);

        await publisher.PublishBatchAsync();
        await fixture.Recorder.WaitAsync(TimeSpan.FromSeconds(15));

        fixture.Recorder.Messages.Should().ContainSingle(item => item.Value == "rabbit");
        context.ChangeTracker.Clear();
        (await context.OutboxMessages.SingleAsync()).Status.Should().Be(OutboxMessageStatus.Published);
    }

    [MessagingIntegrationFact]
    public async Task RepeatedTransportFailure_TransitionsToDeadLetterWithoutInfiniteRetry()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new ManualTimeProvider(now);
        var message = OutboxMessage.Create(
            typeof(RecoveryIntegrationEvent).AssemblyQualifiedName!,
            JsonSerializer.Serialize(new RecoveryIntegrationEvent("dead")),
            now,
            maxRetryCount: 1);
        await using var context = fixture.CreateDbContext();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore(context);
        var failingBus = Substitute.For<IMessageBus>();
        failingBus.PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions?>())
            .Returns(_ => ValueTask.FromException(new InvalidOperationException("broker unavailable")));
        var publisher = new OutboxPublisher(
            store,
            failingBus,
            Options.Create(new OutboxPublisherOptions
            {
                InitialRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromSeconds(10)
            }),
            timeProvider);

        await publisher.PublishBatchAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await publisher.PublishBatchAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var afterDeadLetterCount = await publisher.PublishBatchAsync();

        context.ChangeTracker.Clear();
        var deadLetter = await context.OutboxMessages.SingleAsync();
        deadLetter.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        deadLetter.RetryCount.Should().Be(2);
        afterDeadLetterCount.Should().Be(0);
    }

    private static OutboxMessage CreateOutboxMessage(
        RecoveryIntegrationEvent message,
        DateTimeOffset occurredAtUtc)
        => OutboxMessage.Create(
            message.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(message, message.GetType()),
            occurredAtUtc);

    private static LogAggregateRoot CreateLog()
        => new(
            "recovery",
            "test",
            LogEventTypeEnum.Create,
            "description",
            "content",
            1,
            "127.0.0.1",
            "13800000000");

    private sealed record RecoveryDomainEvent(string Value) : IDomainEvent;
}
