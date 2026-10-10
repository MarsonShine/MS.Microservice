using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MS.Microservice.Core.IO;

namespace MS.Microservice.Notifications.Inbox;

/// <summary>A durable local inbox in a trusted application-owned directory; callers authorize each owner.</summary>
public sealed class LocalNotificationInbox
{
    private readonly string _directory;
    private readonly LocalNotificationInboxOptions _options;
    private readonly TimeProvider _time;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public LocalNotificationInbox(string directory, LocalNotificationInboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _options = options ?? new();
        _time = timeProvider ?? TimeProvider.System;
        if (_options.MaximumNotificationBytes < 1 || _options.MaximumScanEntries < 1
            || _options.MaximumScanBytes < 1 || _options.MaximumPageSize < 1)
            throw new ArgumentException("Inbox budgets must be positive.", nameof(options));
    }

    public async Task<InboxNotification> PublishAsync(string owner, string subject, string body, string? actionPath = null,
        CancellationToken cancellationToken = default)
    {
        var directory = OwnerDirectory(owner);
        ValidateContent(subject, body, actionPath);
        cancellationToken.ThrowIfCancellationRequested();
        var notification = new InboxNotification(StoredFileKey.Create(".json"), _time.GetUtcNow().ToUniversalTime(), subject, body, actionPath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(notification);
        if (bytes.Length > _options.MaximumNotificationBytes) throw new InvalidDataException("The notification exceeds its encoded byte budget.");
        using var content = new MemoryStream(bytes, writable: false);
        await new LocalFileStorage(directory).WriteNewAsync(notification.Id, content, cancellationToken).ConfigureAwait(false);
        return notification;
    }

    public async Task<InboxPage> ListAsync(string owner, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var directory = OwnerDirectory(owner);
        if (skip < 0 || skip > _options.MaximumScanEntries || take < 1 || take > _options.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(take), "Choose a bounded nonnegative offset and positive page size.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory)) return new(Array.Empty<InboxNotification>(), 0, 0);
        var storage = new LocalFileStorage(directory);
        var notifications = new List<InboxNotification>();
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (notifications.Count >= _options.MaximumScanEntries) throw new InvalidDataException("The inbox exceeds its scan entry budget.");
            var key = Path.GetFileName(path);
            StoredFileKey.Validate(key);
            var bytes = await ReadAsync(storage, key, _options.MaximumScanBytes - totalBytes, cancellationToken).ConfigureAwait(false);
            totalBytes += bytes.Length;
            var notification = JsonSerializer.Deserialize<InboxNotification>(bytes)
                ?? throw new InvalidDataException("A stored notification is empty.");
            if (notification.Id != key || notification.CreatedAtUtc.Offset != TimeSpan.Zero || notification.ReadAtUtc is not null)
                throw new InvalidDataException("Stored notification identity or timestamps are invalid.");
            ValidateContent(notification.Subject, notification.Body, notification.ActionPath);
            var receipt = await ReadReceiptAsync(storage, key, _options.MaximumScanBytes - totalBytes, cancellationToken).ConfigureAwait(false);
            totalBytes += receipt.Bytes;
            notifications.Add(notification with { ReadAtUtc = receipt.Time });
        }
        var items = notifications.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .Skip(skip).Take(take).ToArray();
        return new(Array.AsReadOnly(items), notifications.Count, notifications.Count(item => item.ReadAtUtc is null));
    }

    public async Task<DateTimeOffset> MarkReadAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        var storage = new LocalFileStorage(OwnerDirectory(owner));
        ValidateNotificationKey(id);
        cancellationToken.ThrowIfCancellationRequested();
        if (await storage.GetMetadataAsync(id, cancellationToken).ConfigureAwait(false) is null)
            throw new FileNotFoundException("The notification does not exist for this owner.", id);
        var receiptKey = ReceiptKey(id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_time.GetUtcNow().ToUniversalTime());
        if (bytes.Length > _options.MaximumNotificationBytes) throw new InvalidDataException("The read receipt exceeds its encoded byte budget.");
        using var content = new MemoryStream(bytes, writable: false);
        try { await storage.WriteNewAsync(receiptKey, content, cancellationToken).ConfigureAwait(false); }
        catch (IOException) when (File.Exists(Path.Combine(OwnerDirectory(owner), receiptKey))) { }
        var receipt = await ReadReceiptAsync(storage, id, _options.MaximumNotificationBytes, cancellationToken).ConfigureAwait(false);
        return receipt.Time ?? throw new InvalidDataException("The read receipt was not published.");
    }

    private async Task<(DateTimeOffset? Time, int Bytes)> ReadReceiptAsync(LocalFileStorage storage, string id,
        long remainingBytes, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try { bytes = await ReadAsync(storage, ReceiptKey(id), remainingBytes, cancellationToken).ConfigureAwait(false); }
        catch (FileNotFoundException) { return (null, 0); }
        var time = JsonSerializer.Deserialize<DateTimeOffset>(bytes);
        if (time.Offset != TimeSpan.Zero) throw new InvalidDataException("A read receipt must contain UTC time.");
        return (time, bytes.Length);
    }

    private async Task<byte[]> ReadAsync(LocalFileStorage storage, string key, long remainingBytes, CancellationToken cancellationToken)
    {
        await using var input = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        var maximum = Math.Min(_options.MaximumNotificationBytes, remainingBytes);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = (int)Math.Min(buffer.Length, Math.Max(1, maximum - output.Length + 1));
            var read = await input.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (read > maximum - output.Length) throw new InvalidDataException("Inbox data exceeds its encoded byte budget.");
            output.Write(buffer, 0, read);
        }
    }

    private string OwnerDirectory(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 256) throw new ArgumentException("The owner identifier is too long.", nameof(owner));
        return Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(owner))));
    }

    private static void ValidateNotificationKey(string id)
    {
        StoredFileKey.Validate(id);
        if (!id.EndsWith(".json", StringComparison.Ordinal)) throw new ArgumentException("Use a notification JSON key.", nameof(id));
    }

    private static string ReceiptKey(string id) => id[..32] + ".read";

    private static void ValidateContent(string subject, string body, string? actionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(body);
        if (subject.Length > 200 || body.Length > 16_000 || subject.Any(char.IsControl)
            || actionPath is not null && (actionPath.Length > 2048 || !actionPath.StartsWith('/')
                || actionPath.StartsWith("//", StringComparison.Ordinal) || actionPath.Contains('\\') || actionPath.Any(char.IsControl)))
            throw new ArgumentException("Use a bounded plain-text subject/body and optional application-absolute action path.");
        StrictUtf8.GetByteCount(subject);
        StrictUtf8.GetByteCount(body);
        if (actionPath is not null) StrictUtf8.GetByteCount(actionPath);
    }
}
