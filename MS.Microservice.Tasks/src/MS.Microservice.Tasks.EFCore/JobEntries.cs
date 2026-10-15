namespace MS.Microservice.Tasks.EFCore;

internal sealed class JobEntry
{
    public string ScopeHash { get; set; } = "";
    public Guid Id { get; set; }
    public string Scope { get; set; } = "";
    public string HandlerName { get; set; } = "";
    public int HandlerVersion { get; set; }
    public string PayloadJson { get; set; } = "";
    public string Queue { get; set; } = "";
    public int MaximumAttempts { get; set; }
    public long? InitialNotBeforeUtcTicks { get; set; }
    public JobState State { get; set; }
    public int AttemptCount { get; set; }
    public Guid? AttemptToken { get; set; }
    public int Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public bool CancellationRequested { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public long NextRunAtUtcTicks { get; set; }
    public long? FinishedAtUtcTicks { get; set; }
    public string? ErrorCode { get; set; }
    public string? ResultReference { get; set; }
    public long Version { get; set; }

    public JobSnapshot Snapshot() => new(Id, Scope, HandlerName, HandlerVersion, PayloadJson, Queue,
        MaximumAttempts, State, AttemptCount, Progress, ProgressMessage, CancellationRequested,
        At(CreatedAtUtcTicks), At(UpdatedAtUtcTicks), At(NextRunAtUtcTicks),
        FinishedAtUtcTicks is { } end ? At(end) : null, ErrorCode, ResultReference);

    internal static DateTimeOffset At(long ticks) => new(ticks, TimeSpan.Zero);
}

internal sealed class JobAttemptEntry
{
    public string ScopeHash { get; set; } = "";
    public Guid JobId { get; set; }
    public int Number { get; set; }
    public Guid Token { get; set; }
    public JobState State { get; set; }
    public int Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public long StartedAtUtcTicks { get; set; }
    public long? FinishedAtUtcTicks { get; set; }
    public string? ErrorCode { get; set; }
    public string? ResultReference { get; set; }

    public JobAttemptSnapshot Snapshot() => new(Number, State, Progress, ProgressMessage,
        JobEntry.At(StartedAtUtcTicks), FinishedAtUtcTicks is { } end ? JobEntry.At(end) : null,
        ErrorCode, ResultReference);
}
