namespace MS.Microservice.Notifications.Inbox;

public sealed record InboxNotification(string Id, DateTimeOffset CreatedAtUtc, string Subject, string Body,
    string? ActionPath, DateTimeOffset? ReadAtUtc = null);

public sealed record InboxPage(IReadOnlyList<InboxNotification> Items, int TotalCount, int UnreadCount);

public sealed record LocalNotificationInboxOptions
{
    public int MaximumNotificationBytes { get; init; } = 64 * 1024;
    public int MaximumScanEntries { get; init; } = 10_000;
    public long MaximumScanBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumPageSize { get; init; } = 100;
}
