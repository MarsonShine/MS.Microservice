namespace MS.Microservice.Domain.Events;

/// <summary>Inbox processing lifecycle status.</summary>
public enum InboxMessageStatus
{
    /// <summary>The receipt has been recorded.</summary>
    Received = 0,

    /// <summary>The consumer is currently processing the message.</summary>
    Processing = 1,

    /// <summary>The message was processed successfully.</summary>
    Processed = 2,

    /// <summary>The last processing attempt failed.</summary>
    Failed = 3,
}

/// <summary>
/// Durable inbox receipt used for consumer-side deduplication. Setters are public for ORM materialization.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>Creates a new inbox receipt.</summary>
    public static InboxMessage Create(
        Guid messageId,
        string consumer,
        DateTimeOffset receivedAtUtc,
        string? messageType = null,
        string? source = null,
        string? traceId = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        return new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            DeduplicationKey = BuildDeduplicationKey(messageId, consumer),
            MessageType = messageType,
            Source = source,
            ReceivedAtUtc = receivedAtUtc,
            Status = InboxMessageStatus.Received,
            TraceId = traceId,
            CorrelationId = correlationId,
        };
    }

    /// <summary>Builds the stable deduplication key for a message-consumer pair.</summary>
    public static string BuildDeduplicationKey(Guid messageId, string consumer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        return $"{consumer.Trim()}:{messageId:N}";
    }

    /// <summary>Inbound message identifier.</summary>
    public Guid MessageId { get; set; }

    /// <summary>Logical consumer name.</summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>Unique deduplication key, normally persisted with a unique index.</summary>
    public string DeduplicationKey { get; set; } = string.Empty;

    /// <summary>Inbound message contract type.</summary>
    public string? MessageType { get; set; }

    /// <summary>External bus, topic, queue, or provider source.</summary>
    public string? Source { get; set; }

    /// <summary>UTC time when the receipt was recorded.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>UTC time when processing started.</summary>
    public DateTimeOffset? ProcessingStartedAtUtc { get; set; }

    /// <summary>UTC time when processing completed.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Current processing status.</summary>
    public InboxMessageStatus Status { get; set; }

    /// <summary>Number of duplicate deliveries observed for this receipt.</summary>
    public int DuplicateCount { get; set; }

    /// <summary>UTC time when a duplicate delivery was last observed.</summary>
    public DateTimeOffset? LastDuplicateAtUtc { get; set; }

    /// <summary>Last sanitized processing error.</summary>
    public string? LastError { get; set; }

    /// <summary>Distributed trace id associated with the message.</summary>
    public string? TraceId { get; set; }

    /// <summary>Application correlation id associated with the message.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Returns true when the receipt represents the supplied message-consumer pair.</summary>
    public bool Matches(Guid messageId, string consumer) => DeduplicationKey == BuildDeduplicationKey(messageId, consumer);

    /// <summary>Records a duplicate delivery.</summary>
    public void RecordDuplicate(DateTimeOffset nowUtc)
    {
        DuplicateCount++;
        LastDuplicateAtUtc = nowUtc;
    }

    /// <summary>Marks the message as processing.</summary>
    public void MarkProcessing(DateTimeOffset nowUtc)
    {
        Status = InboxMessageStatus.Processing;
        ProcessingStartedAtUtc = nowUtc;
    }

    /// <summary>Marks the message as processed.</summary>
    public void MarkProcessed(DateTimeOffset nowUtc)
    {
        Status = InboxMessageStatus.Processed;
        ProcessedAtUtc = nowUtc;
        LastError = null;
    }

    /// <summary>Marks the message as failed.</summary>
    public void MarkFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Status = InboxMessageStatus.Failed;
        LastError = error;
    }
}
