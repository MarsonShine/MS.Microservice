namespace MS.Microservice.Tasks;

public enum JobState { Queued, Running, Succeeded, Failed, Cancelled, Interrupted }

public sealed record JobSnapshot(Guid JobId, string Scope, string HandlerName, int HandlerVersion, string PayloadJson,
    string Queue, int MaximumAttempts, JobState State, int AttemptCount, int Progress, string? ProgressMessage,
    bool CancellationRequested, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset NextRunAtUtc, DateTimeOffset? FinishedAtUtc, string? ErrorCode, string? ResultReference);

/// <summary>Identifies one execution; retain this token for progress and final-state updates.</summary>
public readonly record struct JobAttemptToken(string Scope, Guid JobId, Guid Token);

public sealed record JobExecution(JobSnapshot Job, JobAttemptToken Attempt);

public sealed record JobAttemptSnapshot(int Number, JobState State, int Progress, string? ProgressMessage,
    DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, string? ErrorCode, string? ResultReference);

public enum JobUpdateResult { Applied, NotFound, Rejected }
public enum JobExecutionStatus { Running, CancellationRequested, Ended }

/// <summary>The same scope and identity cannot represent different job requests.</summary>
public sealed class JobSubmissionConflictException(Guid jobId)
    : InvalidOperationException($"Job {jobId} was already submitted with different parameters.");

/// <summary>Durable submission and lifecycle storage; no method executes the registered job handler.</summary>
public interface IJobStore
{
    /// <summary>Commits a job independently, using a fresh context and no caller-owned transaction.</summary>
    Task<JobSnapshot> SubmitAsync(JobSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>Tracks a job in the caller's active transaction; the caller must save and commit it.</summary>
    Task<JobSnapshot> StageAsync(JobSubmission submission, CancellationToken cancellationToken = default);

    Task<JobSnapshot?> GetAsync(string scope, Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobSnapshot>> ListAsync(string scope, JobState? state = null, int take = 50,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobAttemptSnapshot>> GetAttemptsAsync(string scope, Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads due candidates across scopes for a trusted worker; candidates still require TryStartAsync.</summary>
    Task<IReadOnlyList<JobSnapshot>> GetDueAsync(string queue = "default", int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Reads active execution tokens across scopes for single-instance startup recovery.</summary>
    Task<IReadOnlyList<JobExecution>> GetRunningExecutionsAsync(int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Reads control state for the current execution without loading its payload.</summary>
    Task<JobExecutionStatus> GetExecutionStatusAsync(JobAttemptToken attempt,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically claims a due queued job and appends a new attempt, or returns null.</summary>
    Task<JobExecution?> TryStartAsync(string scope, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Persists monotonic progress for the current running attempt; stale tokens return false.</summary>
    Task<bool> ReportProgressAsync(JobAttemptToken attempt, int progress, string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>Completes the current attempt; an earlier committed cancellation request takes precedence.</summary>
    Task<bool> CompleteAsync(JobAttemptToken attempt, string? resultReference = null,
        CancellationToken cancellationToken = default);
    Task<bool> FailAsync(JobAttemptToken attempt, string errorCode, CancellationToken cancellationToken = default);
    Task<bool> InterruptAsync(JobAttemptToken attempt, CancellationToken cancellationToken = default);

    /// <summary>Atomically interrupts an abandoned attempt and optionally requeues it within its attempt budget.</summary>
    Task<bool> RecoverAsync(JobAttemptToken attempt, bool allowRetry, CancellationToken cancellationToken = default);

    /// <summary>Cancels a queued job, or records a cooperative cancellation request for its running attempt.</summary>
    Task<JobUpdateResult> CancelAsync(string scope, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Requeues a failed, interrupted or cancelled job while retaining its identity and attempt history.</summary>
    Task<JobUpdateResult> RetryAsync(string scope, Guid jobId, DateTimeOffset? notBeforeUtc = null,
        CancellationToken cancellationToken = default);
}
