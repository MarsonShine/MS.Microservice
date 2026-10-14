using System.Text;
using System.Text.Json;

namespace MS.Microservice.Tasks;

/// <summary>A durable job request with frozen JSON; its identity is unique within its scope.</summary>
public sealed class JobSubmission
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public const int MaximumPayloadBytes = 256 * 1024;
    public Guid JobId { get; }
    public string Scope { get; }
    public string HandlerName { get; }
    public int HandlerVersion { get; }
    public string PayloadJson { get; }
    public string Queue { get; }
    public int MaximumAttempts { get; }
    public DateTimeOffset? NotBeforeUtc { get; }

    public JobSubmission(Guid jobId, string scope, string handlerName, int handlerVersion, string payloadJson,
        string queue = "default", int maximumAttempts = 3, DateTimeOffset? notBeforeUtc = null)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job requires a nonempty identity.", nameof(jobId));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 256) throw new ArgumentOutOfRangeException(nameof(scope));
        _ = StrictUtf8.GetByteCount(scope);
        ValidateName(handlerName, 128, nameof(handlerName));
        ValidateName(queue, 64, nameof(queue));
        if (handlerVersion < 1) throw new ArgumentOutOfRangeException(nameof(handlerVersion));
        if (maximumAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payloadJson), "Job payloads must be at most 256 KiB of UTF-8 JSON.");
        using var parsed = JsonDocument.Parse(payloadJson);
        JobId = jobId;
        Scope = scope;
        HandlerName = handlerName;
        HandlerVersion = handlerVersion;
        PayloadJson = payloadJson;
        Queue = queue;
        MaximumAttempts = maximumAttempts;
        NotBeforeUtc = notBeforeUtc?.ToUniversalTime();
    }

    /// <summary>Serializes a payload immediately so later caller changes cannot alter the stored request.</summary>
    public static JobSubmission Create<T>(Guid jobId, string scope, string handlerName, int handlerVersion, T payload,
        string queue = "default", int maximumAttempts = 3, DateTimeOffset? notBeforeUtc = null,
        JsonSerializerOptions? serializerOptions = null)
        => new(jobId, scope, handlerName, handlerVersion, JsonSerializer.Serialize(payload, serializerOptions),
            queue, maximumAttempts, notBeforeUtc);

    internal static void ValidateName(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > maximum || value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
            throw new ArgumentException($"Use at most {maximum} ASCII letters, digits, dots, hyphens or underscores.", parameter);
    }
}
