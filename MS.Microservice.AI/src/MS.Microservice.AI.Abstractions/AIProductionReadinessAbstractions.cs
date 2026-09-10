namespace MS.Microservice.AI.Abstractions;

/// <summary>Context shared by AI production-readiness policies.</summary>
public sealed record AIRequestContext
{
    /// <summary>Logical provider name.</summary>
    public required string Provider { get; init; }

    /// <summary>Resolved model name.</summary>
    public required string Model { get; init; }

    /// <summary>AI capability being invoked.</summary>
    public required AICapability Capability { get; init; }

    /// <summary>Scenario key resolved for the request.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied request id.</summary>
    public string? RequestId { get; init; }
}

/// <summary>Represents a granted AI rate-limit lease.</summary>
public sealed class AIRateLimitLease : IAsyncDisposable
{
    private readonly Func<ValueTask>? _releaseAsync;

    /// <summary>A reusable no-op lease.</summary>
    public static AIRateLimitLease Noop { get; } = new(null);

    /// <summary>Initializes a new instance of <see cref="AIRateLimitLease" />.</summary>
    public AIRateLimitLease(Func<ValueTask>? releaseAsync)
    {
        _releaseAsync = releaseAsync;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _releaseAsync?.Invoke() ?? ValueTask.CompletedTask;
}

/// <summary>Applies provider/model request quota policy before an AI call is sent.</summary>
public interface IAIRateLimiter
{
    /// <summary>Acquires a lease or throws when the request should be rejected.</summary>
    ValueTask<AIRateLimitLease> AcquireAsync(AIRequestContext context, CancellationToken cancellationToken = default);
}

/// <summary>Applies circuit-breaker policy around AI provider calls.</summary>
public interface IAICircuitBreaker
{
    /// <summary>Throws when the circuit is open for the supplied request context.</summary>
    ValueTask EnsureAllowedAsync(AIRequestContext context, CancellationToken cancellationToken = default);

    /// <summary>Records a successful provider call.</summary>
    ValueTask RecordSuccessAsync(AIRequestContext context, CancellationToken cancellationToken = default);

    /// <summary>Records a failed provider call.</summary>
    ValueTask RecordFailureAsync(AIRequestContext context, Exception exception, CancellationToken cancellationToken = default);
}

/// <summary>Redacts secrets and configured sensitive fields before logging or tracing.</summary>
public interface IAILogSanitizer
{
    /// <summary>Returns a sanitized string value.</summary>
    string Sanitize(string? value);

    /// <summary>Returns sanitized key/value metadata.</summary>
    IReadOnlyDictionary<string, string?> SanitizeMetadata(IEnumerable<KeyValuePair<string, string?>> metadata);
}

/// <summary>Describes a secret lookup for an AI provider.</summary>
public sealed record AISecretRequest
{
    /// <summary>Logical provider name.</summary>
    public required string Provider { get; init; }

    /// <summary>Optional configured secret name.</summary>
    public string? SecretName { get; init; }
}

/// <summary>Provider-neutral AI secret source.</summary>
public interface IAISecretProvider
{
    /// <summary>Gets a secret value or <c>null</c> when this provider cannot resolve it.</summary>
    string? GetSecret(AISecretRequest request);
}

/// <summary>AI usage and outcome data emitted after an AI call.</summary>
public sealed record AICostRecord
{
    /// <summary>Logical provider name.</summary>
    public required string Provider { get; init; }

    /// <summary>Resolved model name.</summary>
    public required string Model { get; init; }

    /// <summary>AI capability invoked.</summary>
    public required AICapability Capability { get; init; }

    /// <summary>Scenario key resolved for the request.</summary>
    public string? Scenario { get; init; }

    /// <summary>Caller-supplied request id.</summary>
    public string? RequestId { get; init; }

    /// <summary>Input token count.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output token count.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total token count.</summary>
    public int TotalTokens { get; init; }

    /// <summary>Request duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Whether the provider call succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Canonical exception category, when failed.</summary>
    public string? ExceptionCategory { get; init; }
}

/// <summary>Receives provider/model usage records for metrics, billing, or audit sinks.</summary>
public interface IAICostReporter
{
    /// <summary>Reports one completed AI call.</summary>
    ValueTask ReportAsync(AICostRecord record, CancellationToken cancellationToken = default);
}
