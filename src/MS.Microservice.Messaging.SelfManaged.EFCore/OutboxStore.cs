using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Messaging.SelfManaged;

internal sealed class OutboxStore<TContext>(TContext context, SelfManagedOptions options, TimeProvider clock)
    where TContext : DbContext
{
    private IQueryable<OutboxEntry> Entries => context.Set<OutboxEntry>().AsNoTracking();

    public async Task<IReadOnlyList<OutboxEntry>> ClaimAsync(Guid token, CancellationToken cancellationToken)
    {
        if (token == Guid.Empty) throw new ArgumentException("A claim requires a nonempty token.", nameof(token));
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await Entries.Where(x => (x.State == OutboxState.Pending && x.NextAttemptAtUtc <= now)
                || (x.State == OutboxState.Publishing && x.LockedUntilUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(options.BatchSize)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var claimed = new List<OutboxEntry>();
        foreach (var id in ids)
        {
            // Eligibility and ownership change in one statement; selecting a candidate does not own it.
            var count = await Entries.Where(x => x.Id == id
                    && ((x.State == OutboxState.Pending && x.NextAttemptAtUtc <= now)
                        || (x.State == OutboxState.Publishing && x.LockedUntilUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, OutboxState.Publishing)
                    .SetProperty(x => x.LockToken, token)
                    .SetProperty(x => x.LockedUntilUtc, now + options.PublishingLease), cancellationToken);
            if (count == 1)
            {
                var entry = await Entries.SingleOrDefaultAsync(x => x.Id == id && x.LockToken == token, cancellationToken);
                if (entry is not null) claimed.Add(entry);
            }
        }
        return claimed;
    }

    private IQueryable<OutboxEntry> Owned(Guid id, Guid token, DateTime now)
        => Entries.Where(x => x.Id == id && x.LockToken == token
            && x.State == OutboxState.Publishing && x.LockedUntilUtc > now);

    public async Task<bool> RenewAsync(Guid id, Guid token, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await Owned(id, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.LockedUntilUtc, now + options.PublishingLease), cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(Guid id, Guid token, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await Owned(id, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.State, OutboxState.Published)
            .SetProperty(x => x.CompletedAtUtc, now)
            .SetProperty(x => x.ErrorCode, (string?)null)
            .SetProperty(x => x.LockToken, (Guid?)null)
            .SetProperty(x => x.LockedUntilUtc, (DateTime?)null), cancellationToken) == 1;
    }

    public async Task<bool> FailAsync(OutboxEntry entry, Guid token, string errorCode, bool permanent,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var attempts = entry.AttemptCount + 1;
        var dead = permanent || attempts > options.MaxRetryAttempts;
        return await Owned(entry.Id, token, now).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.State, dead ? OutboxState.DeadLettered : OutboxState.Pending)
            .SetProperty(x => x.AttemptCount, attempts)
            .SetProperty(x => x.NextAttemptAtUtc, now + options.RetryDelay(attempts))
            .SetProperty(x => x.CompletedAtUtc, dead ? now : (DateTime?)null)
            .SetProperty(x => x.ErrorCode, errorCode)
            .SetProperty(x => x.LockToken, (Guid?)null)
            .SetProperty(x => x.LockedUntilUtc, (DateTime?)null), cancellationToken) == 1;
    }

    public async Task<ReplayResult> ReplayAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var count = await Entries.Where(x => x.Id == id && x.State == OutboxState.DeadLettered)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, OutboxState.Pending)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.NextAttemptAtUtc, now)
                .SetProperty(x => x.CompletedAtUtc, (DateTime?)null)
                .SetProperty(x => x.ErrorCode, (string?)null), cancellationToken);
        return count == 1 ? ReplayResult.Accepted
            : await Entries.AnyAsync(x => x.Id == id, cancellationToken) ? ReplayResult.InvalidState : ReplayResult.NotFound;
    }

    public Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - options.PublishedRetention;
        return Entries.Where(x => x.State == OutboxState.Published && x.CompletedAtUtc < cutoff)
            .OrderBy(x => x.CompletedAtUtc).Take(options.BatchSize).ExecuteDeleteAsync(cancellationToken);
    }
}
