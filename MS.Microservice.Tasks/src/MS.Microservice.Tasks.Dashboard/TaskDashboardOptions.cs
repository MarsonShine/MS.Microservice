using Microsoft.AspNetCore.Http;

namespace MS.Microservice.Tasks.Dashboard;

/// <summary>Host-owned authorization and scope selection for the task dashboard.</summary>
public sealed class TaskDashboardOptions
{
    public string Title { get; set; } = "任务中心";
    /// <summary>Resolves a trusted scope from the authenticated request; null or empty denies access.</summary>
    public Func<HttpContext, string?> ResolveScope { get; set; } = null!;
    public string? AuthorizationPolicy { get; set; }
    public string? ControlAuthorizationPolicy { get; set; }
    public string? SchedulesAuthorizationPolicy { get; set; }
    public bool ReadOnly { get; set; }
    /// <summary>Allows the empty UI shell to load before Bearer authentication; API authorization remains required.</summary>
    public bool AllowAnonymousShell { get; set; }
}

/// <summary>A dashboard projection without payload JSON, scope identifiers or execution tokens.</summary>
public sealed record DashboardJob(Guid JobId, string HandlerName, int HandlerVersion, string Queue, JobState State,
    int AttemptCount, int MaximumAttempts, int Progress, string? ProgressMessage, bool CancellationRequested,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset NextRunAtUtc,
    DateTimeOffset? FinishedAtUtc, string? ErrorCode, string? ResultReference)
{
    internal static DashboardJob From(JobSnapshot job) => new(job.JobId, job.HandlerName, job.HandlerVersion,
        job.Queue, job.State, job.AttemptCount, job.MaximumAttempts, job.Progress, job.ProgressMessage,
        job.CancellationRequested, job.CreatedAtUtc, job.UpdatedAtUtc, job.NextRunAtUtc,
        job.FinishedAtUtc, job.ErrorCode, job.ResultReference);
}
