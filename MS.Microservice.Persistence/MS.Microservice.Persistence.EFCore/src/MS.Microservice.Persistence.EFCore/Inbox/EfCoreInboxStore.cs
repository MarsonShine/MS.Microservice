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
        Guid processingToken,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryBeginProcessingAsync(
        string deduplicationKey,
        Guid processingToken,
        DateTimeOffset nowUtc,
        TimeSpan processingLease,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        string deduplicationKey,
        Guid processingToken,
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
        Guid processingToken,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.InboxMessages
            .Where(receipt => receipt.DeduplicationKey == deduplicationKey
                && receipt.ProcessingToken == processingToken
                && receipt.Status == InboxMessageStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(receipt => receipt.Status, InboxMessageStatus.Processed)
                .SetProperty(receipt => receipt.ProcessedAtUtc, processedAtUtc)
                .SetProperty(receipt => receipt.LastError, (string?)null)
                .SetProperty(receipt => receipt.ProcessingToken, (Guid?)null)
                .SetProperty(receipt => receipt.ProcessingLeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryBeginProcessingAsync(
        string deduplicationKey,
        Guid processingToken,
        DateTimeOffset nowUtc,
        TimeSpan processingLease,
        CancellationToken cancellationToken = default)
    {
        if (processingLease <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processingLease));
        }

        var leaseExpiresAtUtc = nowUtc.Add(processingLease);
        var updated = await dbContext.InboxMessages
            .Where(receipt => receipt.DeduplicationKey == deduplicationKey
                && receipt.Status != InboxMessageStatus.Processed
                && (receipt.Status != InboxMessageStatus.Processing
                    || receipt.ProcessingLeaseExpiresAtUtc <= nowUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(receipt => receipt.Status, InboxMessageStatus.Processing)
                .SetProperty(receipt => receipt.ProcessingStartedAtUtc, nowUtc)
                .SetProperty(receipt => receipt.ProcessingToken, processingToken)
                .SetProperty(receipt => receipt.ProcessingLeaseExpiresAtUtc, leaseExpiresAtUtc)
                .SetProperty(receipt => receipt.LastError, (string?)null),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(
        string deduplicationKey,
        Guid processingToken,
        string error,
        CancellationToken cancellationToken = default)
    {
        var sanitizedError = error.Length <= 4000 ? error : error[..4000];
        var updated = await dbContext.InboxMessages
            .Where(receipt => receipt.DeduplicationKey == deduplicationKey
                && receipt.ProcessingToken == processingToken
                && receipt.Status == InboxMessageStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(receipt => receipt.Status, InboxMessageStatus.Failed)
                .SetProperty(receipt => receipt.LastError, sanitizedError)
                .SetProperty(receipt => receipt.ProcessingToken, (Guid?)null)
                .SetProperty(receipt => receipt.ProcessingLeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
        return updated == 1;
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
