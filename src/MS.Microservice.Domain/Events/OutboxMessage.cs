namespace MS.Microservice.Domain.Events;

/// <summary>Outbox publishing lifecycle status.</summary>
public enum OutboxMessageStatus
{
    /// <summary>The message is waiting to be published.</summary>
    Pending = 0,

    /// <summary>A publisher is currently attempting delivery.</summary>
    Publishing = 1,

    /// <summary>The message was published successfully.</summary>
    Published = 2,

    /// <summary>The last publishing attempt failed and can be retried.</summary>
    Failed = 3,

    /// <summary>The message exceeded its retry budget and requires manual handling.</summary>
    DeadLettered = 4,
}

/// <summary>
/// Durable outbox message record. Setters are public to keep EF Core and SqlSugar materialization compatible.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Creates a new pending outbox message.</summary>
    public static OutboxMessage Create(
        string messageType,
        string payload,
        DateTimeOffset occurredAtUtc,
        string contentType = "application/json",
        string? traceId = null,
        string? correlationId = null,
        string? traceParent = null,
        string? traceState = null,
        int maxRetryCount = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (maxRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryCount), "Max retry count cannot be negative.");
        }

        var now = DateTimeOffset.UtcNow;
        return new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            MessageType = messageType,
            Payload = payload,
            ContentType = contentType,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = now,
            Status = OutboxMessageStatus.Pending,
            MaxRetryCount = maxRetryCount,
            TraceId = traceId,
            CorrelationId = correlationId,
            TraceParent = traceParent,
            TraceState = traceState,
        };
    }

    /// <summary>Message identifier used for idempotency and tracing.</summary>
    public Guid MessageId { get; set; }

    /// <summary>CLR or integration contract type name.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Serialized message payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Payload content type.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>UTC time when the event occurred.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>UTC time when the outbox record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Current publishing status.</summary>
    public OutboxMessageStatus Status { get; set; }

    /// <summary>Number of failed publishing attempts.</summary>
    public int RetryCount { get; set; }

    /// <summary>Maximum allowed retry attempts before dead-lettering.</summary>
    public int MaxRetryCount { get; set; } = 10;

    /// <summary>UTC time when the message can be retried.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Token owned by the publisher that claimed this message.</summary>
    public Guid? LockToken { get; set; }

    /// <summary>UTC time after which another publisher may reclaim this message.</summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>UTC time of the latest publishing attempt.</summary>
    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    /// <summary>UTC time when publishing completed.</summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>Last sanitized publishing error.</summary>
    public string? LastError { get; set; }

    /// <summary>Distributed trace id associated with the message.</summary>
    public string? TraceId { get; set; }

    /// <summary>Application correlation id associated with the message.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>W3C parent context captured with the business transaction.</summary>
    public string? TraceParent { get; set; }

    /// <summary>W3C vendor trace state captured with the business transaction.</summary>
    public string? TraceState { get; set; }

    /// <summary>Marks the message as currently being published.</summary>
    public void MarkPublishing(DateTimeOffset nowUtc)
    {
        Status = OutboxMessageStatus.Publishing;
        LastAttemptAtUtc = nowUtc;
    }

    /// <summary>Claims the message for one publisher until the supplied UTC lease expires.</summary>
    public void Claim(Guid lockToken, DateTimeOffset nowUtc, DateTimeOffset lockedUntilUtc)
    {
        if (lockedUntilUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(lockedUntilUtc), "Lock expiry must be after the claim time.");
        }

        MarkPublishing(nowUtc);
        LockToken = lockToken;
        LockedUntilUtc = lockedUntilUtc;
    }

    /// <summary>Marks the message as published and clears retry metadata.</summary>
    public void MarkPublished(DateTimeOffset nowUtc)
    {
        Status = OutboxMessageStatus.Published;
        PublishedAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        LastError = null;
        LockToken = null;
        LockedUntilUtc = null;
    }

    /// <summary>Records a failed publishing attempt and schedules retry or dead-letter.</summary>
    public void MarkFailed(string error, DateTimeOffset nowUtc, TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay), "Retry delay cannot be negative.");
        }

        RetryCount++;
        LastAttemptAtUtc = nowUtc;
        LastError = error;
        Status = RetryCount > MaxRetryCount ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Failed;
        NextAttemptAtUtc = Status == OutboxMessageStatus.Failed ? nowUtc.Add(retryDelay) : null;
        LockToken = null;
        LockedUntilUtc = null;
    }

    /// <summary>Resets a dead-lettered message for an explicit operator replay.</summary>
    public void Replay(DateTimeOffset nowUtc)
    {
        if (Status != OutboxMessageStatus.DeadLettered)
        {
            throw new InvalidOperationException("Only dead-lettered outbox messages can be replayed.");
        }

        Status = OutboxMessageStatus.Pending;
        RetryCount = 0;
        NextAttemptAtUtc = nowUtc;
        LastAttemptAtUtc = null;
        PublishedAtUtc = null;
        LastError = null;
        LockToken = null;
        LockedUntilUtc = null;
    }
}
