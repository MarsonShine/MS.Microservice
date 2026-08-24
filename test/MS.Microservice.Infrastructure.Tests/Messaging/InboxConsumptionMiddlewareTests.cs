using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Messaging;
using MS.Microservice.Domain.Events;
using MS.Microservice.Infrastructure.Messaging;
using MS.Microservice.Persistence.EFCore.Inbox;
using NSubstitute;
using Wolverine;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Messaging;

public sealed class InboxConsumptionMiddlewareTests
{
    [Fact]
    public async Task BeforeAndAfter_FirstDelivery_ContinuesAndMarksProcessed()
    {
        var store = Substitute.For<IInboxStore>();
        var transactionCoordinator = CreateTransactionCoordinator(out var transaction);
        var message = new TestIntegrationEvent();
        var envelope = CreateEnvelope(message.Id);
        var receipt = InboxMessage.Create(message.Id, ConsumerName, DateTimeOffset.UtcNow);
        store.TryRegisterAsync(message.Id, ConsumerName, Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InboxRegistration(true, receipt));
        store.TryBeginProcessingAsync(receipt.DeduplicationKey, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        store.MarkProcessedAsync(receipt.DeduplicationKey, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var (continuation, execution) = await BeforeAsync(message, envelope, store, transactionCoordinator);
        await InboxConsumptionMiddleware.AfterAsync(execution, store, transactionCoordinator, TimeProvider.System, CancellationToken.None);
        await InboxConsumptionMiddleware.FinallyAsync(execution, store, Substitute.For<ILogger<InboxExecution>>(), CancellationToken.None);

        Assert.Equal(HandlerContinuation.Continue, continuation);
        Assert.True(execution.Completed);
        await store.Received(1).MarkProcessedAsync(receipt.DeduplicationKey, execution.ProcessingToken, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().MarkFailedAsync(default!, default, default!, default);
        await transactionCoordinator.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Before_DuplicateWithActiveOrCompletedReceipt_StopsHandler()
    {
        var store = Substitute.For<IInboxStore>();
        var transactionCoordinator = CreateTransactionCoordinator(out _);
        var message = new TestIntegrationEvent();
        var envelope = CreateEnvelope(message.Id);
        var receipt = InboxMessage.Create(message.Id, ConsumerName, DateTimeOffset.UtcNow);
        receipt.MarkProcessed(DateTimeOffset.UtcNow);
        store.TryRegisterAsync(message.Id, ConsumerName, Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InboxRegistration(false, receipt));
        store.TryBeginProcessingAsync(receipt.DeduplicationKey, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var (continuation, execution) = await BeforeAsync(message, envelope, store, transactionCoordinator);

        Assert.Equal(HandlerContinuation.Stop, continuation);
        Assert.False(execution.ShouldExecute);
        await transactionCoordinator.DidNotReceiveWithAnyArgs().BeginAsync(default);
    }

    [Fact]
    public async Task Before_OutboxRetry_UsesStableHeaderInsteadOfNewEnvelopeId()
    {
        var store = Substitute.For<IInboxStore>();
        var transactionCoordinator = CreateTransactionCoordinator(out _);
        var message = new TestIntegrationEvent();
        var stableMessageId = Guid.NewGuid();
        var envelope = CreateEnvelope(Guid.NewGuid());
        envelope.Headers[MessageHeaders.MessageId] = stableMessageId.ToString("N");
        var receipt = InboxMessage.Create(stableMessageId, ConsumerName, DateTimeOffset.UtcNow);
        store.TryRegisterAsync(stableMessageId, ConsumerName, Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new InboxRegistration(false, receipt));

        await BeforeAsync(message, envelope, store, transactionCoordinator);

        await store.Received(1).TryRegisterAsync(
            stableMessageId,
            ConsumerName,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Finally_WhenHandlerDidNotComplete_MarksOwnedReceiptFailed()
    {
        var store = Substitute.For<IInboxStore>();
        var transaction = Substitute.For<IInboxTransaction>();
        var execution = new InboxExecution("consumer:key", Guid.NewGuid(), true, transaction);

        await InboxConsumptionMiddleware.FinallyAsync(
            execution,
            store,
            Substitute.For<ILogger<InboxExecution>>(),
            CancellationToken.None);

        await transaction.Received(1).RollbackAsync(CancellationToken.None);
        await transaction.Received(1).DisposeAsync();
        await store.Received(1).MarkFailedAsync(
            execution.DeduplicationKey,
            execution.ProcessingToken,
            "Handler execution did not complete successfully.",
            Arg.Any<CancellationToken>());
    }

    private static Task<(HandlerContinuation, InboxExecution)> BeforeAsync(
        TestIntegrationEvent message,
        Envelope envelope,
        IInboxStore store,
        IInboxTransactionCoordinator transactionCoordinator)
        => InboxConsumptionMiddleware.BeforeAsync(
            message,
            envelope,
            store,
            transactionCoordinator,
            Options.Create(new InboxConsumerOptions()),
            TimeProvider.System,
            CancellationToken.None);

    private static IInboxTransactionCoordinator CreateTransactionCoordinator(
        out IInboxTransaction transaction)
    {
        transaction = Substitute.For<IInboxTransaction>();
        var coordinator = Substitute.For<IInboxTransactionCoordinator>();
        coordinator.BeginAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        return coordinator;
    }

    private static Envelope CreateEnvelope(Guid messageId)
        => new()
        {
            Id = messageId,
            EndpointName = "orders",
            MessageType = typeof(TestIntegrationEvent).AssemblyQualifiedName
        };

    private const string ConsumerName = "orders:MS.Microservice.Infrastructure.Tests.Messaging.InboxConsumptionMiddlewareTests+TestIntegrationEvent";

    public sealed class TestIntegrationEvent : IntegrationEvent;
}
