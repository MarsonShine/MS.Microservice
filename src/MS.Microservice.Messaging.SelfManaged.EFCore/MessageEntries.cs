namespace MS.Microservice.Messaging.SelfManaged;

internal enum OutboxState { Pending, Publishing, Published, DeadLettered }
internal enum InboxState { Processing, Processed, Failed, DeadLettered }

internal sealed class OutboxEntry
{
    public Guid Id { get; set; }
    public string ContractName { get; set; } = "";
    public int ContractVersion { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Payload { get; set; } = "";
    public string? CorrelationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public OutboxState State { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }

    public static OutboxEntry From(SerializedMessage message, DateTime now) => new()
    {
        Id = message.Id, ContractName = message.ContractName, ContractVersion = message.ContractVersion,
        OccurredAtUtc = message.OccurredAtUtc.UtcDateTime, Payload = message.Payload,
        CorrelationId = message.CorrelationId, TraceParent = message.TraceParent, TraceState = message.TraceState,
        CreatedAtUtc = now, NextAttemptAtUtc = now
    };

    public SerializedMessage ToMessage() => new(Id, ContractName, ContractVersion,
        new DateTimeOffset(DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc)), Payload,
        CorrelationId, TraceParent, TraceState);
}

internal sealed class InboxEntry
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = "";
    public string ContractName { get; set; } = "";
    public int ContractVersion { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Payload { get; set; } = "";
    public string? CorrelationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public InboxState State { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorCode { get; set; }

    public SerializedMessage ToMessage() => new(MessageId, ContractName, ContractVersion,
        new DateTimeOffset(DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc)), Payload,
        CorrelationId, TraceParent, TraceState);
}
