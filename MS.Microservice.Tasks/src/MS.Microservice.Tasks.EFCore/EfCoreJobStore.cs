using System.Security.Cryptography;
using System.Text;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Tasks.EFCore;

/// <summary>Stores jobs in the host model; Stage joins its transaction and all other operations use fresh scopes.</summary>
public sealed class EfCoreJobStore<TContext>(TContext stagingContext, IServiceScopeFactory scopes, TimeProvider clock)
    : IJobStore where TContext : DbContext
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public async Task<JobSnapshot> SubmitAsync(JobSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        try
        {
            return await IndependentlyAsync(async context =>
            {
                var existing = await FindAsync(context, submission.Scope, submission.JobId, cancellationToken);
                if (existing is not null) return Match(existing, submission);
                var entry = NewEntry(submission);
                context.Add(entry);
                await context.SaveChangesAsync(cancellationToken);
                return entry.Snapshot();
            });
        }
        catch (DbUpdateException)
        {
            // A competing submit can win the unique key. Query only after disposing the failed context.
            var existing = await IndependentlyAsync(context => FindAsync(context, submission.Scope, submission.JobId, cancellationToken));
            if (existing is null) throw;
            return Match(existing, submission);
        }
    }

    public async Task<JobSnapshot> StageAsync(JobSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        EnsureModel(stagingContext);
        if (stagingContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stage requires the caller's active DbContext transaction; use SubmitAsync for independent submission.");
        cancellationToken.ThrowIfCancellationRequested();
        var hash = ScopeHash(submission.Scope);
        var existing = stagingContext.Set<JobEntry>().Local.SingleOrDefault(entry => entry.ScopeHash == hash && entry.Id == submission.JobId)
            ?? await FindAsync(stagingContext, submission.Scope, submission.JobId, cancellationToken);
        if (existing is not null) return Match(existing, submission);
        var job = NewEntry(submission);
        stagingContext.Add(job);
        return job.Snapshot();
    }

    public Task<JobSnapshot?> GetAsync(string scope, Guid jobId, CancellationToken cancellationToken = default)
        => IndependentlyAsync(async context => (await FindAsync(context, scope, jobId, cancellationToken))?.Snapshot());

    public Task<IReadOnlyList<JobSnapshot>> ListAsync(string scope, JobState? state = null, int take = 50,
        CancellationToken cancellationToken = default)
    {
        var hash = ScopeHash(scope);
        ValidateTake(take);
        if (state is { } value && !Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(state));
        return IndependentlyAsync<IReadOnlyList<JobSnapshot>>(async context =>
        {
            var query = Jobs(context).Where(entry => entry.ScopeHash == hash);
            if (state is { } filter) query = query.Where(entry => entry.State == filter);
            var entries = await query.OrderByDescending(entry => entry.CreatedAtUtcTicks).ThenBy(entry => entry.Id)
                .Take(take).ToListAsync(cancellationToken);
            return entries.Select(entry => entry.Snapshot()).ToArray();
        });
    }

    public Task<IReadOnlyList<JobAttemptSnapshot>> GetAttemptsAsync(string scope, Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var hash = Identity(scope, jobId);
        return IndependentlyAsync<IReadOnlyList<JobAttemptSnapshot>>(async context =>
        {
            var entries = await context.Set<JobAttemptEntry>().AsNoTracking()
                .Where(entry => entry.ScopeHash == hash && entry.JobId == jobId)
                .OrderBy(entry => entry.Number).ToListAsync(cancellationToken);
            return entries.Select(entry => entry.Snapshot()).ToArray();
        });
    }

    public Task<IReadOnlyList<JobSnapshot>> GetDueAsync(string queue = "default", int take = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        if (queue.Length > 64) throw new ArgumentOutOfRangeException(nameof(queue));
        ValidateTake(take);
        var now = clock.GetUtcNow().UtcTicks;
        return IndependentlyAsync<IReadOnlyList<JobSnapshot>>(async context =>
        {
            var entries = await Jobs(context).Where(entry => entry.Queue == queue && entry.State == JobState.Queued
                    && !entry.CancellationRequested && entry.AttemptCount < entry.MaximumAttempts && entry.NextRunAtUtcTicks <= now)
                .OrderBy(entry => entry.NextRunAtUtcTicks).ThenBy(entry => entry.CreatedAtUtcTicks).ThenBy(entry => entry.Id)
                .Take(take).ToListAsync(cancellationToken);
            return entries.Select(entry => entry.Snapshot()).ToArray();
        });
    }

    public Task<JobExecution?> TryStartAsync(string scope, Guid jobId, CancellationToken cancellationToken = default)
    {
        var hash = Identity(scope, jobId);
        return IndependentlyAsync<JobExecution?>(async context =>
        {
            var now = clock.GetUtcNow().UtcTicks;
            var token = Guid.NewGuid();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var changed = await Jobs(context).Where(entry => entry.ScopeHash == hash && entry.Id == jobId
                    && entry.State == JobState.Queued && !entry.CancellationRequested
                    && entry.AttemptCount < entry.MaximumAttempts && entry.NextRunAtUtcTicks <= now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.State, JobState.Running)
                    .SetProperty(entry => entry.AttemptCount, entry => entry.AttemptCount + 1)
                    .SetProperty(entry => entry.AttemptToken, token)
                    .SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                    .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            if (changed == 0) return null;
            var job = await Jobs(context).SingleAsync(entry => entry.ScopeHash == hash && entry.Id == jobId, cancellationToken);
            context.Add(new JobAttemptEntry
            {
                ScopeHash = hash, JobId = jobId, Number = job.AttemptCount, Token = token,
                State = JobState.Running, StartedAtUtcTicks = now
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(job.Snapshot(), new(scope, jobId, token));
        });
    }

    public Task<IReadOnlyList<JobExecution>> GetRunningExecutionsAsync(int take = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateTake(take);
        return IndependentlyAsync<IReadOnlyList<JobExecution>>(async context =>
        {
            var entries = await Jobs(context).Where(entry => entry.State == JobState.Running)
                .OrderBy(entry => entry.CreatedAtUtcTicks).ThenBy(entry => entry.Id).Take(take).ToListAsync(cancellationToken);
            return entries.Select(entry => new JobExecution(entry.Snapshot(),
                new(entry.Scope, entry.Id, entry.AttemptToken
                    ?? throw new InvalidOperationException("A running job has no execution token.")))).ToArray();
        });
    }

    public Task<JobExecutionStatus> GetExecutionStatusAsync(JobAttemptToken attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        return IndependentlyAsync(async context =>
        {
            var requested = await Owned(context, attempt).Select(entry => (bool?)entry.CancellationRequested)
                .SingleOrDefaultAsync(cancellationToken);
            return requested is null ? JobExecutionStatus.Ended
                : requested.Value ? JobExecutionStatus.CancellationRequested : JobExecutionStatus.Running;
        });
    }

    public Task<bool> ReportProgressAsync(JobAttemptToken attempt, int progress, string? message = null,
        CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        if (progress is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(progress));
        if (message?.Length > 1024) throw new ArgumentOutOfRangeException(nameof(message));
        return IndependentlyAsync(async context =>
        {
            var now = clock.GetUtcNow().UtcTicks;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var changed = await Owned(context, attempt).Where(entry => !entry.CancellationRequested && entry.Progress <= progress)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Progress, progress)
                    .SetProperty(entry => entry.ProgressMessage, message)
                    .SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                    .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            if (changed == 0) return false;
            var updated = await CurrentAttempt(context, attempt).ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.Progress, progress).SetProperty(entry => entry.ProgressMessage, message), cancellationToken);
            RequireAttempt(updated);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public Task<bool> CompleteAsync(JobAttemptToken attempt, string? resultReference = null,
        CancellationToken cancellationToken = default)
    {
        if (resultReference is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resultReference);
            if (resultReference.Length > 4096) throw new ArgumentOutOfRangeException(nameof(resultReference));
        }
        return FinishAsync(attempt, JobState.Succeeded, null, resultReference, cancellationToken);
    }

    public Task<bool> FailAsync(JobAttemptToken attempt, string errorCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (errorCode.Length > 128) throw new ArgumentOutOfRangeException(nameof(errorCode));
        return FinishAsync(attempt, JobState.Failed, errorCode, null, cancellationToken);
    }

    public Task<bool> InterruptAsync(JobAttemptToken attempt, CancellationToken cancellationToken = default)
        => FinishAsync(attempt, JobState.Interrupted, "interrupted", null, cancellationToken);

    public Task<bool> RecoverAsync(JobAttemptToken attempt, bool allowRetry, CancellationToken cancellationToken = default)
        => FinishAsync(attempt, JobState.Interrupted, "interrupted", null, cancellationToken, allowRetry);

    public Task<JobUpdateResult> CancelAsync(string scope, Guid jobId, CancellationToken cancellationToken = default)
    {
        var hash = Identity(scope, jobId);
        return IndependentlyAsync(async context =>
        {
            var now = clock.GetUtcNow().UtcTicks;
            var query = Jobs(context).Where(entry => entry.ScopeHash == hash && entry.Id == jobId);
            // Separate the transitions so SET evaluation order cannot change their meaning across providers.
            var changed = await query.Where(entry => entry.State == JobState.Queued)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.CancellationRequested, true)
                    .SetProperty(entry => entry.FinishedAtUtcTicks, now)
                    .SetProperty(entry => entry.State, JobState.Cancelled)
                    .SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                    .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            if (changed == 1) return JobUpdateResult.Applied;
            changed = await query.Where(entry => entry.State == JobState.Running)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.CancellationRequested, true)
                    .SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                    .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            if (changed == 1) return JobUpdateResult.Applied;
            var existing = await query.SingleOrDefaultAsync(cancellationToken);
            return existing is null ? JobUpdateResult.NotFound
                : existing.State == JobState.Cancelled ? JobUpdateResult.Applied : JobUpdateResult.Rejected;
        });
    }

    public Task<JobUpdateResult> RetryAsync(string scope, Guid jobId, DateTimeOffset? notBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        var hash = Identity(scope, jobId);
        return IndependentlyAsync(async context =>
        {
            var now = clock.GetUtcNow().UtcTicks;
            var next = notBeforeUtc?.UtcTicks ?? now;
            var query = Jobs(context).Where(entry => entry.ScopeHash == hash && entry.Id == jobId);
            var changed = await query.Where(entry => (entry.State == JobState.Failed || entry.State == JobState.Cancelled
                    || entry.State == JobState.Interrupted) && entry.AttemptCount < entry.MaximumAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.State, JobState.Queued)
                    .SetProperty(entry => entry.CancellationRequested, false)
                    .SetProperty(entry => entry.AttemptToken, (Guid?)null)
                    .SetProperty(entry => entry.Progress, 0).SetProperty(entry => entry.ProgressMessage, (string?)null)
                    .SetProperty(entry => entry.FinishedAtUtcTicks, (long?)null)
                    .SetProperty(entry => entry.ErrorCode, (string?)null).SetProperty(entry => entry.ResultReference, (string?)null)
                    .SetProperty(entry => entry.NextRunAtUtcTicks, next).SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                    .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            return changed == 1 ? JobUpdateResult.Applied
                : await query.AnyAsync(cancellationToken) ? JobUpdateResult.Rejected : JobUpdateResult.NotFound;
        });
    }

    private Task<bool> FinishAsync(JobAttemptToken attempt, JobState finalState, string? errorCode,
        string? resultReference, CancellationToken cancellationToken, bool requeueInterrupted = false)
    {
        ValidateAttempt(attempt);
        return IndependentlyAsync(async context =>
        {
            var now = clock.GetUtcNow().UtcTicks;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            // Evaluate cancellation in the same UPDATE that publishes success: the first committed transition wins.
            var changed = await Owned(context, attempt).ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.State, entry => entry.CancellationRequested ? JobState.Cancelled : finalState)
                .SetProperty(entry => entry.Progress, entry => !entry.CancellationRequested && finalState == JobState.Succeeded ? 100 : entry.Progress)
                .SetProperty(entry => entry.ErrorCode, entry => entry.CancellationRequested ? null : errorCode)
                .SetProperty(entry => entry.ResultReference, entry => entry.CancellationRequested ? null : resultReference)
                .SetProperty(entry => entry.AttemptToken, (Guid?)null)
                .SetProperty(entry => entry.FinishedAtUtcTicks, now).SetProperty(entry => entry.UpdatedAtUtcTicks, now)
                .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            if (changed == 0) return false;
            var job = await FindAsync(context, attempt.Scope, attempt.JobId, cancellationToken)
                ?? throw new InvalidOperationException("A completed job disappeared inside its transaction.");
            var updated = await CurrentAttempt(context, attempt).ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.State, job.State).SetProperty(entry => entry.Progress, job.Progress)
                .SetProperty(entry => entry.ProgressMessage, job.ProgressMessage).SetProperty(entry => entry.FinishedAtUtcTicks, now)
                .SetProperty(entry => entry.ErrorCode, job.ErrorCode).SetProperty(entry => entry.ResultReference, job.ResultReference), cancellationToken);
            RequireAttempt(updated);
            if (requeueInterrupted && job.State == JobState.Interrupted && job.AttemptCount < job.MaximumAttempts)
            {
                var hash = ScopeHash(attempt.Scope);
                await Jobs(context).Where(entry => entry.ScopeHash == hash && entry.Id == attempt.JobId
                        && entry.State == JobState.Interrupted).ExecuteUpdateAsync(setters => setters
                        .SetProperty(entry => entry.State, JobState.Queued).SetProperty(entry => entry.Progress, 0)
                        .SetProperty(entry => entry.ProgressMessage, (string?)null).SetProperty(entry => entry.ErrorCode, (string?)null)
                        .SetProperty(entry => entry.FinishedAtUtcTicks, (long?)null).SetProperty(entry => entry.NextRunAtUtcTicks, now)
                        .SetProperty(entry => entry.Version, entry => entry.Version + 1), cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    private async Task<T> IndependentlyAsync<T>(Func<TContext, Task<T>> operation)
    {
        if (stagingContext.Database.CurrentTransaction is not null || Transaction.Current is not null)
            throw new InvalidOperationException("Independent job operations cannot join an external transaction; stage submissions instead.");
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        if (ReferenceEquals(context, stagingContext) || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Register a clean scoped DbContext so independent job operations cannot commit unrelated changes.");
        EnsureModel(context);
        return await operation(context);
    }

    private JobEntry NewEntry(JobSubmission submission)
    {
        var now = clock.GetUtcNow().UtcTicks;
        return new JobEntry
        {
            ScopeHash = ScopeHash(submission.Scope), Scope = submission.Scope, Id = submission.JobId,
            HandlerName = submission.HandlerName, HandlerVersion = submission.HandlerVersion,
            PayloadJson = submission.PayloadJson, Queue = submission.Queue, MaximumAttempts = submission.MaximumAttempts,
            InitialNotBeforeUtcTicks = submission.NotBeforeUtc?.UtcTicks,
            CreatedAtUtcTicks = now, UpdatedAtUtcTicks = now, NextRunAtUtcTicks = submission.NotBeforeUtc?.UtcTicks ?? now
        };
    }

    private static JobSnapshot Match(JobEntry existing, JobSubmission submission)
    {
        if (existing.Scope != submission.Scope || existing.HandlerName != submission.HandlerName
            || existing.HandlerVersion != submission.HandlerVersion || existing.PayloadJson != submission.PayloadJson
            || existing.Queue != submission.Queue || existing.MaximumAttempts != submission.MaximumAttempts
            || existing.InitialNotBeforeUtcTicks != submission.NotBeforeUtc?.UtcTicks)
            throw new JobSubmissionConflictException(submission.JobId);
        return existing.Snapshot();
    }

    private static IQueryable<JobEntry> Jobs(TContext context) => context.Set<JobEntry>().AsNoTracking();
    private static Task<JobEntry?> FindAsync(TContext context, string scope, Guid id, CancellationToken token)
    {
        var hash = Identity(scope, id);
        return Jobs(context).SingleOrDefaultAsync(entry => entry.ScopeHash == hash && entry.Id == id, token);
    }

    private static IQueryable<JobEntry> Owned(TContext context, JobAttemptToken attempt)
    {
        var hash = ScopeHash(attempt.Scope);
        return Jobs(context).Where(entry => entry.ScopeHash == hash && entry.Id == attempt.JobId
            && entry.State == JobState.Running && entry.AttemptToken == attempt.Token);
    }

    private static IQueryable<JobAttemptEntry> CurrentAttempt(TContext context, JobAttemptToken attempt)
    {
        var hash = ScopeHash(attempt.Scope);
        return context.Set<JobAttemptEntry>().Where(entry => entry.ScopeHash == hash && entry.JobId == attempt.JobId
            && entry.Token == attempt.Token && entry.State == JobState.Running);
    }

    private static string ScopeHash(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 256) throw new ArgumentOutOfRangeException(nameof(scope));
        return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(scope)));
    }

    private static string Identity(string scope, Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A job requires a nonempty identity.", nameof(id));
        return ScopeHash(scope);
    }

    private static void ValidateAttempt(JobAttemptToken attempt)
    {
        _ = Identity(attempt.Scope, attempt.JobId);
        if (attempt.Token == Guid.Empty) throw new ArgumentException("An execution requires a nonempty token.", nameof(attempt));
    }

    private static void ValidateTake(int take)
    {
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
    }

    private static void EnsureModel(TContext context)
    {
        if (context.Model.FindEntityType(typeof(JobEntry)) is null || context.Model.FindEntityType(typeof(JobAttemptEntry)) is null)
            throw new InvalidOperationException("Call AddTaskCenter in the host DbContext model before using the job store.");
    }

    private static void RequireAttempt(int updated)
    {
        if (updated != 1) throw new InvalidOperationException("The current job and its attempt history are inconsistent.");
    }
}
