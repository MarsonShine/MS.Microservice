using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.DbContext;
using Npgsql;

namespace MS.Microservice.Persistence.EFCore.Inbox;

public sealed record InboxRegistration(bool IsFirstDelivery, InboxMessage Receipt);

public interface IInboxStore
{
    Task<InboxRegistration> TryRegisterAsync(
        Guid messageId,
        string consumer,
        DateTimeOffset receivedAtUtc,
        string? messageType = null,
        string? source = null,
        string? traceId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(
        string deduplicationKey,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        string deduplicationKey,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class EfCoreInboxStore(ActivationDbContext dbContext) : IInboxStore
{
    public async Task<InboxRegistration> TryRegisterAsync(
        Guid messageId,
        string consumer,
        DateTimeOffset receivedAtUtc,
        string? messageType = null,
        string? source = null,
        string? traceId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var deduplicationKey = InboxMessage.BuildDeduplicationKey(messageId, consumer);
        var existing = await dbContext.InboxMessages.FindAsync([deduplicationKey], cancellationToken);
        if (existing is not null)
        {
            return await RecordDuplicateAsync(existing, receivedAtUtc, cancellationToken);
        }

        var receipt = InboxMessage.Create(
            messageId,
            consumer,
            receivedAtUtc,
            messageType,
            source,
            traceId,
            correlationId);
        dbContext.InboxMessages.Add(receipt);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new InboxRegistration(true, receipt);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(receipt).State = EntityState.Detached;
            var concurrentReceipt = await dbContext.InboxMessages
                .SingleAsync(message => message.DeduplicationKey == deduplicationKey, cancellationToken);
            return await RecordDuplicateAsync(concurrentReceipt, receivedAtUtc, cancellationToken);
        }
    }

    public async Task<bool> MarkProcessedAsync(
        string deduplicationKey,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var receipt = await dbContext.InboxMessages.FindAsync([deduplicationKey], cancellationToken);
        if (receipt is null)
        {
            return false;
        }

        receipt.MarkProcessed(processedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkFailedAsync(
        string deduplicationKey,
        string error,
        CancellationToken cancellationToken = default)
    {
        var receipt = await dbContext.InboxMessages.FindAsync([deduplicationKey], cancellationToken);
        if (receipt is null)
        {
            return false;
        }

        receipt.MarkFailed(error);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<InboxRegistration> RecordDuplicateAsync(
        InboxMessage receipt,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        receipt.RecordDuplicate(receivedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new InboxRegistration(false, receipt);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
}
