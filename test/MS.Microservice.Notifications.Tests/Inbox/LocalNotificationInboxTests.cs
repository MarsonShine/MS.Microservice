using System.Text.Json;
using MS.Microservice.Notifications.Inbox;
using Xunit;

namespace MS.Microservice.Notifications.Tests.Inbox;

public sealed class LocalNotificationInboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.LocalNotificationInboxTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 30, 0, TimeSpan.Zero);
    private LocalNotificationInbox Inbox(string? directory = null, LocalNotificationInboxOptions? options = null, DateTimeOffset? now = null)
        => new(directory ?? _root, options, new FixedTime(now ?? Now));

    [Fact]
    public async Task NotificationsAndFirstReadReceipt_SurviveNewInstancesAndRemainOwnerScoped()
    {
        var message = await Inbox().PublishAsync("user-a", "处理完成", "文件已生成\n第二行", "/tools/jobs/1");
        await Inbox().PublishAsync("user-b", "另一用户", "私有内容");
        var page = await Inbox().ListAsync("user-a");
        Assert.Equal((1, 1), (page.TotalCount, page.UnreadCount));
        Assert.Equal(message, Assert.Single(page.Items));
        var firstRead = await Inbox(now: Now.AddMinutes(1)).MarkReadAsync("user-a", message.Id);
        var repeatedRead = await Inbox(now: Now.AddMinutes(2)).MarkReadAsync("user-a", message.Id);
        Assert.Equal(Now.AddMinutes(1), firstRead);
        Assert.Equal(firstRead, repeatedRead);
        var reloaded = await Inbox().ListAsync("user-a");
        Assert.Equal((1, 0), (reloaded.TotalCount, reloaded.UnreadCount));
        Assert.Equal(firstRead, Assert.Single(reloaded.Items).ReadAtUtc);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Inbox().MarkReadAsync("user-b", message.Id));
        Assert.Equal(1, (await Inbox().ListAsync("user-b")).UnreadCount);
    }

    [Fact]
    public async Task ConcurrentPublicationsAndReadReceipts_HaveDistinctMessagesAndOneReadTime()
    {
        var published = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Inbox().PublishAsync("owner", "消息" + index, "内容")));
        Assert.Equal(16, published.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        var marked = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Inbox(now: Now.AddSeconds(index)).MarkReadAsync("owner", published[0].Id)));
        Assert.All(marked, time => Assert.Equal(marked[0], time));
        var page = await Inbox().ListAsync("owner");
        Assert.Equal((16, 15), (page.TotalCount, page.UnreadCount));
        Assert.Single(Directory.GetFiles(OwnerDirectory(), "*.read"));
        Assert.Empty(Directory.GetFiles(OwnerDirectory(), ".*.tmp"));
    }

    [Fact]
    public async Task Paging_UsesCreatedTimeAndStableIdTieBreakAndReportsWholeInboxCounts()
    {
        var first = await Inbox().PublishAsync("owner", "first", "body");
        var second = await Inbox(now: Now.AddSeconds(1)).PublishAsync("owner", "second", "body");
        var third = await Inbox(now: Now.AddSeconds(1)).PublishAsync("owner", "third", "body");
        await Inbox().MarkReadAsync("owner", first.Id);
        var expected = new[] { second, third }.OrderByDescending(item => item.Id, StringComparer.Ordinal).Select(item => item.Id).Append(first.Id).ToArray();
        var page = await Inbox().ListAsync("owner", skip: 1, take: 1);
        Assert.Equal(expected[1], Assert.Single(page.Items).Id);
        Assert.Equal((3, 2), (page.TotalCount, page.UnreadCount));
        Assert.Empty((await Inbox().ListAsync("owner", skip: 10)).Items);
    }

    [Fact]
    public async Task ExactEncodedBudgetsWorkAndOneByteOverIsRejectedWithoutPublication()
    {
        var sample = await Inbox().PublishAsync("sample", "subject", "body");
        var length = JsonSerializer.SerializeToUtf8Bytes(sample).Length;
        var directory = Path.Combine(_root, "exact");
        var limited = Inbox(directory, new() { MaximumNotificationBytes = length });
        await limited.PublishAsync("owner", "subject", "body");
        await Assert.ThrowsAsync<InvalidDataException>(() => limited.PublishAsync("owner", "subject!", "body"));
        Assert.Equal(1, (await limited.ListAsync("owner")).TotalCount);
    }

    [Fact]
    public async Task ScanByteBudget_IncludesReceiptsAndFailsRatherThanReturningPartialCounts()
    {
        var message = await Inbox().PublishAsync("owner", "subject", "body");
        await Inbox().MarkReadAsync("owner", message.Id);
        await Inbox().PublishAsync("owner", "second", "body");
        var length = Directory.GetFiles(OwnerDirectory()).Sum(path => new FileInfo(path).Length);
        Assert.Equal(2, (await Inbox(options: new() { MaximumScanBytes = length }).ListAsync("owner")).TotalCount);
        await Assert.ThrowsAsync<InvalidDataException>(() => Inbox(options: new() { MaximumScanBytes = length - 1 }).ListAsync("owner"));
    }

    [Fact]
    public async Task ScanEntryBudget_AndPageBudgetAreExplicit()
    {
        await Inbox().PublishAsync("owner", "one", "body");
        await Inbox().PublishAsync("owner", "two", "body");
        Assert.Equal(2, (await Inbox(options: new() { MaximumScanEntries = 2 }).ListAsync("owner")).TotalCount);
        await Inbox().PublishAsync("owner", "three", "body");
        await Assert.ThrowsAsync<InvalidDataException>(() => Inbox(options: new() { MaximumScanEntries = 2 }).ListAsync("owner"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Inbox(options: new() { MaximumPageSize = 2 }).ListAsync("owner", take: 3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Inbox().ListAsync("owner", skip: -1));
    }

    [Fact]
    public async Task MissingOwner_AndPreCancelledOperationsDoNotCreateFiles()
    {
        Assert.Equal(0, (await Inbox().ListAsync("owner")).TotalCount);
        Assert.False(Directory.Exists(_root));
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inbox().PublishAsync("owner", "subject", "body", cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inbox().ListAsync("owner", cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inbox().MarkReadAsync("owner", new string('a', 32) + ".json", token));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("/tools/1")]
    [InlineData("/tools/1?q=你好")]
    [InlineData(null)]
    public async Task ValidApplicationActionPathsAndPlainTextArePreserved(string? path)
    {
        var message = await Inbox().PublishAsync("owner", "subject", "<tag>{{token}} 😀\n", path);
        Assert.Equal(message, Assert.Single((await Inbox().ListAsync("owner")).Items));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\outside")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/route\r\nheader")]
    public async Task InvalidActionPathsFailWithoutCreatingStorage(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Inbox().PublishAsync("owner", "subject", "body", path));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task OwnerIdentifiersAreOpaqueAndNeverUsedAsPathsOrNormalizedTogether()
    {
        foreach (var owner in new[] { "../outside", "OWNER", "owner", " owner ", "é", "e\u0301" })
            await Inbox().PublishAsync(owner, owner, "body");
        Assert.Equal(6, Directory.GetDirectories(_root).Length);
        Assert.Equal("../outside", Assert.Single((await Inbox().ListAsync("../outside")).Items).Subject);
        Assert.Equal("owner", Assert.Single((await Inbox().ListAsync("owner")).Items).Subject);
    }

    [Fact]
    public async Task TemporaryFilesAreIgnoredButCorruptPublishedDataIsNotHidden()
    {
        var message = await Inbox().PublishAsync("owner", "subject", "body");
        await File.WriteAllTextAsync(Path.Combine(OwnerDirectory(), ".partial.tmp"), "partial");
        Assert.Equal(1, (await Inbox().ListAsync("owner")).TotalCount);
        await File.WriteAllTextAsync(Path.Combine(OwnerDirectory(), message.Id), "{ broken");
        await Assert.ThrowsAsync<JsonException>(() => Inbox().ListAsync("owner"));
    }

    [Fact]
    public async Task CorruptReadReceiptDoesNotResetTheMessageToUnread()
    {
        var message = await Inbox().PublishAsync("owner", "subject", "body");
        await File.WriteAllTextAsync(Path.Combine(OwnerDirectory(), message.Id[..32] + ".read"), "null");
        await Assert.ThrowsAsync<JsonException>(() => Inbox().ListAsync("owner"));
        await Assert.ThrowsAsync<JsonException>(() => Inbox().MarkReadAsync("owner", message.Id));
    }

    [Fact]
    public async Task InvalidUnicodeAndAliasKeysAreRejected()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Inbox().PublishAsync("owner", "subject", "bad\ud800"));
        await Assert.ThrowsAsync<ArgumentException>(() => Inbox().MarkReadAsync("owner", "../outside.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => Inbox().MarkReadAsync("owner", new string('a', 32) + ".read"));
        Assert.False(Directory.Exists(_root));
    }

    private string OwnerDirectory() => Assert.Single(Directory.GetDirectories(_root));
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.LocalNotificationInboxTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
