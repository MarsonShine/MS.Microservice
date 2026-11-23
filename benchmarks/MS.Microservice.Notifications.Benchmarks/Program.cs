using MS.Microservice.Notifications.Inbox;
using MS.Microservice.Notifications.Sms;
using MS.Microservice.Notifications.Templates;

var sender = new AliyunSmsSender(new AliyunSmsOptions
{
    AccessKeyId = "benchmark",
    AccessKeySecret = "benchmark",
    MaximumTemplateBytes = 1
});
var oversized = new SmsMessage("+000000", "Example", "SMS_test")
{
    TemplateParameters = Enumerable.Range(0, 32).ToDictionary(i => $"key{i:D2}", i => $"value{i:D2}")
};
MeasuredOperation.Run("SMS / 32 parameters over byte budget", 256, RejectOversizedMessage,
    error => error?.ParamName == "message" && error.Message.Contains("encoded byte limit", StringComparison.Ordinal));
if (args is ["--sms-only"])
{
    return;
}

var values = new Dictionary<string, string?> { ["Recipient"] = "王", ["Status"] = "A&B" };
string literal = string.Concat(Enumerable.Repeat("本周系统维护完成。", 64));
MeasuredOperation.Run("Template / literal", 4096,
    () => NamedTokenTemplate.Render(literal, values), result => result == literal);
const string mixed = "To {{Recipient}}: {{Status}}.";
MeasuredOperation.Run("Template / two HTML tokens", 4096,
    () => NamedTokenTemplate.Render(mixed, values, htmlEncodeValues: true), result => result == "To 王: A&amp;B.");

var directory = Directory.CreateTempSubdirectory("ms-inbox-benchmark-");
try
{
    const string owner = "benchmark-owner";
    var inbox = new LocalNotificationInbox(directory.FullName,
        new LocalNotificationInboxOptions { MaximumPageSize = 128 });
    var notifications = new List<InboxNotification>();
    string body = new('x', 2048);
    for (int i = 0; i < 128; i++)
    {
        notifications.Add(await inbox.PublishAsync(owner, $"Reminder {i:D3}", body));
    }
    CheckPage("Inbox / unread page 8", 0, 8);

    for (int i = 0; i < notifications.Count; i += 2)
    {
        DateTimeOffset timestamp = await inbox.MarkReadAsync(owner, notifications[i].Id);
        notifications[i] = notifications[i] with { ReadAtUtc = timestamp };
    }
    CheckPage("Inbox / half read page 8", 0, 8);
    CheckPage("Inbox / half read deep page", 120, 8);
    CheckPage("Inbox / half read full page", 0, 128);
    var read = notifications[0];
    MeasuredOperation.Run("Inbox / repeat MarkRead", 16,
        () => inbox.MarkReadAsync(owner, read.Id).GetAwaiter().GetResult(), receipt => receipt == read.ReadAtUtc);

    void CheckPage(string name, int skip, int take)
    {
        var expected = notifications.OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal).Skip(skip).Take(take).ToArray();
        int unread = notifications.Count(item => item.ReadAtUtc is null);
        MeasuredOperation.Run(name, 2,
            () => inbox.ListAsync(owner, skip, take).GetAwaiter().GetResult(),
            page => page.TotalCount == notifications.Count && page.UnreadCount == unread && page.Items.SequenceEqual(expected));
    }
}
finally
{
    directory.Delete(recursive: true);
}

ArgumentException? RejectOversizedMessage()
{
    try
    {
        sender.SendAsync(oversized).GetAwaiter().GetResult();
        return null;
    }
    catch (ArgumentException error)
    {
        return error;
    }
}
