namespace MS.Microservice.Documents.Internal;

internal sealed class BoundedBuffer(int maximumBytes, string format = "Document") : MemoryStream
{
    internal async Task ReadFromAsync(Stream input, CancellationToken cancellationToken)
    {
        if (RemainingMemoryLength(input, maximumBytes - Length) is not null)
        {
            await input.CopyToAsync(this, cancellationToken).ConfigureAwait(false);
            return;
        }
        var scratch = new byte[(int)Math.Min(81920L, (long)maximumBytes + 1)];
        int read;
        while ((read = await input.ReadAsync(scratch.AsMemory(0,
            (int)Math.Min(scratch.Length, (long)maximumBytes - Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            Write(scratch, 0, read);
        }
    }

    internal static async Task<byte[]> ReadSnapshotAsync(Stream input, int maximumBytes, string format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RemainingMemoryLength(input, maximumBytes) is { } remaining)
        {
            var snapshot = new byte[remaining];
            await input.ReadExactlyAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        using var data = new BoundedBuffer(maximumBytes, format);
        await data.ReadFromAsync(input, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return data.ToArray();
    }

    private static int? RemainingMemoryLength(Stream input, long limit)
    {
        // A MemoryStream subclass can override length, position or read behavior.
        if (input.GetType() != typeof(MemoryStream))
        {
            return null;
        }
        long remaining = Math.Max(0, input.Length - input.Position);
        return remaining <= limit ? (int)remaining : null;
    }

    internal void ChangeWriteLimit(int newMaximumBytes, string newFormat)
    {
        if (Length > newMaximumBytes)
        {
            throw new InvalidDataException($"{newFormat} data exceeds the configured byte limit.");
        }
        if (Capacity > newMaximumBytes)
        {
            Capacity = newMaximumBytes;
        }
        maximumBytes = newMaximumBytes;
        format = newFormat;
    }

    private void Reserve(int count)
    {
        if (count > maximumBytes - Position)
            throw new InvalidDataException($"{format} data exceeds the configured byte limit.");
        var required = (int)(Position + count);
        if (required > Capacity)
            Capacity = (int)Math.Min(maximumBytes, Math.Max(required, Math.Max(256L, Capacity * 2L)));
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Reserve(count);
        base.Write(buffer, offset, count);
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Reserve(buffer.Length);
        base.Write(buffer);
    }
    public override void WriteByte(byte value)
    {
        Reserve(1);
        base.WriteByte(value);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override void SetLength(long value)
    {
        if (value > maximumBytes) throw new InvalidDataException($"{format} data exceeds the configured byte limit.");
        if (value > Capacity)
            Capacity = (int)Math.Min(maximumBytes, Math.Max(value, Math.Max(256L, Capacity * 2L)));
        base.SetLength(value);
    }
}
