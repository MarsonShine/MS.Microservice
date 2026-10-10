namespace MS.Microservice.Documents.Internal;

internal sealed class BoundedBuffer(int maximumBytes, string format = "Document") : MemoryStream
{
    private void Reserve(int count)
    {
        if (count > maximumBytes - Position) throw new InvalidDataException($"{format} data exceeds the configured byte limit.");
        var required = (int)(Position + count);
        if (required > Capacity)
            Capacity = (int)Math.Min(maximumBytes, Math.Max(required, Math.Max(256L, Capacity * 2L)));
    }

    public override void Write(byte[] buffer, int offset, int count) { Reserve(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Reserve(1); base.WriteByte(value); }
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
