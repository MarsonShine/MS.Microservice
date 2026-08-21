namespace MS.Microservice.Infrastructure.Messaging;

public sealed class InboxConsumerOptions
{
    public const string SectionName = "InboxConsumer";

    public TimeSpan ProcessingLease { get; set; } = TimeSpan.FromMinutes(5);
}
