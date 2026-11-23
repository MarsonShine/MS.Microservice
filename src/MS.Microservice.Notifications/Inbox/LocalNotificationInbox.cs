using System.Buffers;
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
    private static readonly IComparer<InboxNotification> OldestFirst = Comparer<InboxNotification>.Create((left, right) =>
    {
        int byTime = left.CreatedAtUtc.CompareTo(right.CreatedAtUtc);
        return byTime != 0 ? byTime : string.CompareOrdinal(left.Id, right.Id);
    });

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
        var buffer = new ArrayBufferWriter<byte>();
        var newest = new PriorityQueue<InboxNotification, InboxNotification>(OldestFirst);
        long pageEnd = (long)skip + take;
        int total = 0;
        int unread = 0;
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total >= _options.MaximumScanEntries)
            {
                throw new InvalidDataException("The inbox exceeds its scan entry budget.");
            }
            var key = Path.GetFileName(path);
            StoredFileKey.Validate(key);
            var bytes = await ReadAsync(path, buffer, _options.MaximumScanBytes - totalBytes, cancellationToken).ConfigureAwait(false);
            totalBytes += bytes.Length;
            var notification = JsonSerializer.Deserialize<InboxNotification>(bytes.Span)
                ?? throw new InvalidDataException("A stored notification is empty.");
            if (notification.Id != key || notification.CreatedAtUtc.Offset != TimeSpan.Zero || notification.ReadAtUtc is not null)
                throw new InvalidDataException("Stored notification identity or timestamps are invalid.");
            ValidateContent(notification.Subject, notification.Body, notification.ActionPath);
            var receipt = await ReadReceiptAsync(Path.Combine(directory, ReceiptKey(key)), buffer,
                _options.MaximumScanBytes - totalBytes, cancellationToken).ConfigureAwait(false);
            totalBytes += receipt.Bytes;
            total++;
            if (receipt.Time is { } readAt)
            {
                notification = notification with { ReadAtUtc = readAt };
            }
            else
            {
                unread++;
            }
            if (newest.Count < pageEnd)
            {
                newest.Enqueue(notification, notification);
            }
            else
            {
                newest.EnqueueDequeue(notification, notification);
            }
        }
        // The oldest retained items belong to the requested page; the remaining newer ones are skipped.
        var items = new InboxNotification[Math.Max(0, newest.Count - skip)];
        for (int index = items.Length - 1; index >= 0; index--)
        {
            items[index] = newest.Dequeue();
        }
        return new(Array.AsReadOnly(items), total, unread);
    }

    public async Task<DateTimeOffset> MarkReadAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        var directory = OwnerDirectory(owner);
        var storage = new LocalFileStorage(directory);
        ValidateNotificationKey(id);
        cancellationToken.ThrowIfCancellationRequested();
        if (await storage.GetMetadataAsync(id, cancellationToken).ConfigureAwait(false) is null)
            throw new FileNotFoundException("The notification does not exist for this owner.", id);
        var receiptKey = ReceiptKey(id);
        var path = Path.Combine(directory, receiptKey);
        var buffer = new ArrayBufferWriter<byte>();
        var existing = await ReadReceiptAsync(path, buffer, _options.MaximumNotificationBytes, cancellationToken).ConfigureAwait(false);
        if (existing.Time is { } firstRead)
        {
            return firstRead;
        }
        var time = _time.GetUtcNow().ToUniversalTime();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(time);
        if (bytes.Length > _options.MaximumNotificationBytes) throw new InvalidDataException("The read receipt exceeds its encoded byte budget.");
        using var content = new MemoryStream(bytes, writable: false);
        try
        {
            await storage.WriteNewAsync(receiptKey, content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return time;
        }
        catch (IOException) when (File.Exists(path))
        {
            var winner = await ReadReceiptAsync(path, buffer, _options.MaximumNotificationBytes, cancellationToken).ConfigureAwait(false);
            return winner.Time ?? throw new InvalidDataException("The read receipt was not published.");
        }
    }

    private async ValueTask<(DateTimeOffset? Time, int Bytes)> ReadReceiptAsync(string path, ArrayBufferWriter<byte> buffer,
        long remainingBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // FileSystemInfo reports missing entries as -1 and propagates errors reading attributes.
        if (new FileInfo(path).Attributes == (FileAttributes)(-1))
        {
            return default;
        }
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = await ReadAsync(path, buffer, remainingBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return default;
        }
        var time = JsonSerializer.Deserialize<DateTimeOffset>(bytes.Span);
        if (time.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("A read receipt must contain UTC time.");
        }
        return (time, bytes.Length);
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string path, ArrayBufferWriter<byte> buffer,
        long remainingBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = input.Length;
        if (length > Math.Min(_options.MaximumNotificationBytes, remainingBytes))
        {
            throw new InvalidDataException("Inbox data exceeds its encoded byte budget.");
        }
        var memory = buffer.GetMemory(Math.Max(1, (int)length));
        var data = memory[..(int)length];
        await input.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        // Published files are immutable. EOF leaves data intact; any extra byte invalidates this read.
        if (await input.ReadAsync(memory[..1], cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("The published inbox file changed while being read.");
        }
        return data;
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
