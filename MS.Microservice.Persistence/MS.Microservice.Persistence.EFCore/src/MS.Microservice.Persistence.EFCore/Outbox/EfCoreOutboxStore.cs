using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.DbContext;

namespace MS.Microservice.Persistence.EFCore.Outbox;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        Guid lockToken,
        DateTimeOffset nowUtc,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    Task<bool> MarkPublishedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        Guid messageId,
        Guid lockToken,
        string error,
        DateTimeOffset nowUtc,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task<bool> ReplayDeadLetterAsync(
        Guid messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed class EfCoreOutboxStore(ActivationDbContext dbContext) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        Guid lockToken,
        DateTimeOffset nowUtc,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.OutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM "fz_platform_activation"."OutboxMessages"
                WHERE (
                    "Status" IN ('Pending', 'Failed')
                    AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {{nowUtc}})
                ) OR (
                    "Status" = 'Publishing'
                    AND "LockedUntilUtc" <= {{nowUtc}}
                )
                ORDER BY "OccurredAtUtc", "MessageId"
                LIMIT {{batchSize}}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var lockedUntilUtc = nowUtc.Add(lockDuration);
        foreach (var message in messages)
        {
            message.Claim(lockToken, nowUtc, lockedUntilUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    public async Task<bool> MarkPublishedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var message = await FindOwnedMessageAsync(messageId, lockToken, cancellationToken);
        if (message is null)
        {
            return false;
        }

        message.MarkPublished(nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkFailedAsync(
        Guid messageId,
        Guid lockToken,
        string error,
        DateTimeOffset nowUtc,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var message = await FindOwnedMessageAsync(messageId, lockToken, cancellationToken);
        if (message is null)
        {
            return false;
        }

        message.MarkFailed(error, nowUtc, retryDelay);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReplayDeadLetterAsync(
        Guid messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.OutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.MessageId == messageId
                && candidate.Status == OutboxMessageStatus.DeadLettered,
            cancellationToken);
        if (message is null)
        {
            return false;
        }

        message.Replay(nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<OutboxMessage?> FindOwnedMessageAsync(
        Guid messageId,
        Guid lockToken,
        CancellationToken cancellationToken)
        => dbContext.OutboxMessages.SingleOrDefaultAsync(
            message => message.MessageId == messageId
                && message.LockToken == lockToken
                && message.Status == OutboxMessageStatus.Publishing,
            cancellationToken);
}
