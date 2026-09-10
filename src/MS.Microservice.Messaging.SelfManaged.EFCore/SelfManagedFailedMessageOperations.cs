using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class SelfManagedFailedMessageOperations<TContext>(TContext context, OutboxStore<TContext> outbox,
    MessageContractRegistry registry, TimeProvider clock, ILogger<SelfManagedFailedMessageOperations<TContext>> logger)
    : IFailedMessageOperations where TContext : DbContext
{
    public async Task<IReadOnlyList<FailedMessage>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var outgoing = await context.Set<OutboxEntry>().AsNoTracking().Where(x => x.State == OutboxState.DeadLettered)
            .OrderBy(x => x.CompletedAtUtc).Take(limit).ToListAsync(cancellationToken);
        var incoming = await context.Set<InboxEntry>().AsNoTracking().Where(x => x.State == InboxState.DeadLettered)
            .OrderBy(x => x.CompletedAtUtc).Take(limit).ToListAsync(cancellationToken);
        return outgoing.Select(x => new FailedMessage($"outbox:{x.Id:N}", x.Id, x.ContractName, x.ContractVersion,
                null, Utc(x.CompletedAtUtc ?? x.CreatedAtUtc), x.ErrorCode ?? "unknown"))
            .Concat(incoming.Select(x => new FailedMessage($"inbox:{x.MessageId:N}:{Uri.EscapeDataString(x.Consumer)}",
                x.MessageId, x.ContractName, x.ContractVersion, x.Consumer,
                Utc(x.CompletedAtUtc ?? x.ReceivedAtUtc), x.ErrorCode ?? "unknown")))
            .OrderBy(x => x.FailedAtUtc).Take(limit).ToArray();
    }

    public async Task<ReplayResult> ReplayAsync(string failureId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureId);
        var parts = failureId.Split(':', 3);
        if (parts.Length < 2 || !Guid.TryParseExact(parts[1], "N", out var id)) return ReplayResult.NotFound;
        ReplayResult result;
        if (parts[0] == "outbox" && parts.Length == 2)
            result = await outbox.ReplayAsync(id, cancellationToken);
        else if (parts[0] == "inbox" && parts.Length == 3)
            result = await ReplayInboxAsync(id, Uri.UnescapeDataString(parts[2]), cancellationToken);
        else return ReplayResult.NotFound;
        logger.LogInformation("Message replay {ReplayResult} for {FailureId}", result, failureId);
        return result;
    }

    private async Task<ReplayResult> ReplayInboxAsync(Guid id, string consumer, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Replay must own its administrative transaction.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var entry = await context.Set<InboxEntry>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.MessageId == id && x.Consumer == consumer, cancellationToken);
        if (entry is null) return ReplayResult.NotFound;
        if (entry.State != InboxState.DeadLettered) return ReplayResult.InvalidState;
        var message = entry.ToMessage();
        try { registry.Deserialize(message); }
        catch (MessageContractException) { return ReplayResult.InvalidState; }
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await context.Set<InboxEntry>()
            .Where(x => x.MessageId == id && x.Consumer == consumer && x.State == InboxState.DeadLettered)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, InboxState.Failed)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.NextAttemptAtUtc, now)
                .SetProperty(x => x.CompletedAtUtc, (DateTime?)null)
                .SetProperty(x => x.ErrorCode, (string?)null), cancellationToken);
        if (updated != 1) return ReplayResult.InvalidState;
        var existing = await context.Set<OutboxEntry>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null) context.Add(OutboxEntry.From(message, now));
        else
        {
            if (existing.Payload != message.Payload || existing.ContractName != message.ContractName
                || existing.ContractVersion != message.ContractVersion)
                return ReplayResult.InvalidState;
            // Fence any old publisher so a delivery before this transaction commits cannot consume the replay.
            await context.Set<OutboxEntry>().Where(x => x.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, OutboxState.Pending)
                .SetProperty(x => x.LockToken, (Guid?)null)
                .SetProperty(x => x.LockedUntilUtc, (DateTime?)null)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.NextAttemptAtUtc, now)
                .SetProperty(x => x.CompletedAtUtc, (DateTime?)null)
                .SetProperty(x => x.ErrorCode, (string?)null), cancellationToken);
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReplayResult.Accepted;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
