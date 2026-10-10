namespace MS.Microservice.Notifications.Sms;

public sealed class AliyunSmsOptions
{
    public required string AccessKeyId { get; init; }
    public required string AccessKeySecret { get; init; }
    public string? SecurityToken { get; init; }
    public int TimeoutMilliseconds { get; init; } = 30_000;
    public int MaximumTemplateBytes { get; init; } = 16 * 1024;
}
