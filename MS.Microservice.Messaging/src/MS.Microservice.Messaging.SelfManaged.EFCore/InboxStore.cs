using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

internal enum InboxAcquisition { Acquired, AlreadyProcessed, Busy, DeadLettered }
internal sealed record InboxClaim(InboxAcquisition Result, InboxEntry Entry);

internal sealed class InboxStore<TContext>(TContext context, SelfManagedOptions options, TimeProvider clock)
    where TContext : DbContext
{
    private IQueryable<InboxEntry> Entries => context.Set<InboxEntry>().AsNoTracking();

    public async Task<InboxClaim> AcquireAsync(SerializedMessage message, string consumer, Guid token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        if (consumer.Length > 200 || token == Guid.Empty) throw new ArgumentException("Invalid consumer or claim token.");
        MessageMetadataLimits.Validate(message.CorrelationId, message.TraceParent, message.TraceState);
        var now = clock.GetUtcNow().UtcDateTime;
        var entry = await FindAsync(message.Id, consumer, cancellationToken);
        if (entry is null)
        {
            entry = new InboxEntry
            {
                MessageId = message.Id, Consumer = consumer, ContractName = message.ContractName,
                ContractVersion = message.ContractVersion, OccurredAtUtc = message.OccurredAtUtc.UtcDateTime,
                Payload = message.Payload, CorrelationId = message.CorrelationId,
                TraceParent = message.TraceParent, TraceState = message.TraceState,
                State = InboxState.Processing, LockToken = token, LockedUntilUtc = now + options.ProcessingLease,
                ReceivedAtUtc = now, NextAttemptAtUtc = now
            };
            context.Add(entry);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.Entry(entry).State = EntityState.Detached;
                return new(InboxAcquisition.Acquired, entry);
            }
            catch (DbUpdateException)
            {
                context.Entry(entry).State = EntityState.Detached;
                // Only a concurrent insertion of this receipt is recoverable here.
                entry = await FindAsync(message.Id, consumer, cancellationToken);
                if (entry is null) throw;
            }
        }

        if (entry.ContractName != message.ContractName || entry.ContractVersion != message.ContractVersion
            || entry.Payload != message.Payload)
            throw new MessageContractException("A received message Id identifies conflicting content.");
        if (entry.State == InboxState.Processed) return new(InboxAcquisition.AlreadyProcessed, entry);
        if (entry.State == InboxState.DeadLettered) return new(InboxAcquisition.DeadLettered, entry);

        var count = await Entries.Where(x => x.MessageId == message.Id && x.Consumer == consumer
                && ((x.State == InboxState.Processing && x.LockedUntilUtc <= now)
                    || (x.State == InboxState.Failed && x.NextAttemptAtUtc <= now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, InboxState.Processing)
                .SetProperty(x => x.LockToken, token)
                .SetProperty(x => x.LockedUntilUtc, now + options.ProcessingLease), cancellationToken);
        if (count == 0) return new(InboxAcquisition.Busy, entry);
        entry.State = InboxState.Processing;
        entry.LockToken = token;
        entry.LockedUntilUtc = now + options.ProcessingLease;
        return new(InboxAcquisition.Acquired, entry);
    }

    public Task<InboxEntry?> FindAsync(Guid id, string consumer, CancellationToken cancellationToken)
        => Entries.SingleOrDefaultAsync(x => x.MessageId == id && x.Consumer == consumer, cancellationToken);

    private IQueryable<InboxEntry> Owned(Guid id, string consumer, Guid token, DateTime now)
        => Entries.Where(x => x.MessageId == id && x.Consumer == consumer && x.LockToken == token
            && x.State == InboxState.Processing && x.LockedUntilUtc > now);

    public async Task<bool> RenewAsync(Guid id, string consumer, Guid token, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await Owned(id, consumer, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.LockedUntilUtc, now + options.ProcessingLease), cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(Guid id, string consumer, Guid token, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inbox completion must participate in the business transaction.");
        var now = clock.GetUtcNow().UtcDateTime;
        return await Owned(id, consumer, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.State, InboxState.Processed)
            .SetProperty(x => x.CompletedAtUtc, now)
            .SetProperty(x => x.LockToken, (Guid?)null)
            .SetProperty(x => x.LockedUntilUtc, (DateTime?)null)
            .SetProperty(x => x.ErrorCode, (string?)null), cancellationToken) == 1;
    }

    public async Task<bool> FailAsync(InboxEntry entry, Guid token, string errorCode, bool permanent,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var attempts = entry.AttemptCount + 1;
        var dead = permanent || attempts > options.MaxRetryAttempts;
        return await Owned(entry.MessageId, entry.Consumer, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.State, dead ? InboxState.DeadLettered : InboxState.Failed)
            .SetProperty(x => x.AttemptCount, attempts)
            .SetProperty(x => x.NextAttemptAtUtc, now + options.RetryDelay(attempts))
            .SetProperty(x => x.CompletedAtUtc, dead ? now : (DateTime?)null)
            .SetProperty(x => x.ErrorCode, errorCode)
            .SetProperty(x => x.LockToken, (Guid?)null)
            .SetProperty(x => x.LockedUntilUtc, (DateTime?)null), cancellationToken) == 1;
    }

    public Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - options.ProcessedRetention;
        return Entries.Where(x => x.State == InboxState.Processed && x.CompletedAtUtc < cutoff)
            .OrderBy(x => x.CompletedAtUtc).Take(options.BatchSize).ExecuteDeleteAsync(cancellationToken);
    }
}
