using System.Text.Json;

namespace MS.Microservice.Tasks;

/// <summary>Runs one registered payload contract and returns an optional private result reference.</summary>
public interface IJobHandler<in TPayload>
{
    Task<string?> ExecuteAsync(TPayload payload, JobExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Describes the current execution and saves progress using its execution token.</summary>
public sealed class JobExecutionContext
{
    private readonly Func<int, string?, Task<bool>> _report;
    public Guid JobId { get; }
    public string Scope { get; }
    public int AttemptNumber { get; }

    internal JobExecutionContext(JobSnapshot job, Func<int, string?, Task<bool>> report)
    {
        JobId = job.JobId;
        Scope = job.Scope;
        AttemptNumber = job.AttemptCount;
        _report = report;
    }

    public Task<bool> ReportProgressAsync(int progress, string? message = null) => _report(progress, message);
}

public sealed class JobHandlerOptions
{
    public string Queue { get; set; } = "default";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool SafeToRetry { get; set; }
    public JsonSerializerOptions? SerializerOptions { get; set; }
}

public sealed class JobExecutionOptions
{
    private readonly Dictionary<string, int> _queues = new(StringComparer.Ordinal) { ["default"] = 1 };
    public TimeSpan CancellationPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan StorageOperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan InitialFinalizationRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumFinalizationRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    internal IReadOnlyDictionary<string, int> Queues => _queues;

    /// <summary>Sets the maximum executions whose handlers, cleanup or final-state reconciliation are still active in a resource queue.</summary>
    public void AddQueue(string queue, int concurrency)
    {
        JobSubmission.ValidateName(queue, 64, nameof(queue));
        if (concurrency is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(concurrency));
        _queues[queue] = concurrency;
    }
}
