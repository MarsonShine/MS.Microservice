namespace MS.Microservice.Infrastructure.Messaging;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public int BatchSize { get; set; } = 50;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan FailureRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
