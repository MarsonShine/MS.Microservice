namespace MS.Microservice.Messaging.SelfManaged;

public sealed class SelfManagedOptions
{
    public int BatchSize { get; set; } = 50;
    public int MaxRetryAttempts { get; set; } = 10;
    public TimeSpan DiagnosticsInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PublishingLease { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ProcessingLease { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan BusyRecheckInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan BusyWaitLimit { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan BrokerAcknowledgementTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PublishedRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan ProcessedRetention { get; set; } = TimeSpan.FromDays(30);

    public void Validate()
    {
        if (BatchSize is < 1 or > 1000 || MaxRetryAttempts is < 0 or > 100)
            throw new ArgumentException("BatchSize must be 1..1000 and MaxRetryAttempts 0..100.");
        if (DiagnosticsInterval <= TimeSpan.Zero || PollInterval <= TimeSpan.Zero || PublishingLease < TimeSpan.FromSeconds(1)
            || ProcessingLease < TimeSpan.FromSeconds(1) || InitialRetryDelay <= TimeSpan.Zero
            || MaximumRetryDelay < InitialRetryDelay || BusyRecheckInterval <= TimeSpan.Zero
            || BusyWaitLimit < BusyRecheckInterval || ProcessingTimeout <= TimeSpan.Zero
            || BrokerAcknowledgementTimeout <= ProcessingTimeout + BusyWaitLimit
            || PublishedRetention <= TimeSpan.Zero || ProcessedRetention <= TimeSpan.Zero)
            throw new ArgumentException("Messaging durations must be positive, ordered, and fit the broker acknowledgment timeout.");
    }

    public TimeSpan RetryDelay(int failureCount)
        => TimeSpan.FromTicks((long)Math.Min(MaximumRetryDelay.Ticks,
            InitialRetryDelay.Ticks * Math.Pow(2, Math.Clamp(failureCount - 1, 0, 30))));
}
