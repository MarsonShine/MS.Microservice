namespace MS.Microservice.Documents.Internal;

/// <summary>Counts decompressed bytes consumed by an XML reader, including its internal read-ahead.</summary>
internal sealed class ExpandedPartStream(Stream source, long limit, CancellationToken operationToken) : Stream
{
    private bool _finished;
    public long BytesRead { get; private set; }
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private int ReadSize(int requested)
    {
        long remaining = limit - BytesRead;
        return remaining >= requested ? requested : (int)remaining + 1;
    }

    private int Account(int read, int requested)
    {
        if (read > limit - BytesRead)
        {
            throw new InvalidDataException("Expanded DOCX exceeds the byte limit.");
        }
        BytesRead += read;
        _finished = read == 0 && requested != 0;
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        operationToken.ThrowIfCancellationRequested();
        // If the underlying read or budget check fails, cleanup must not drain past that failure.
        _finished = true;
        return Account(source.Read(buffer[..ReadSize(buffer.Length)]), buffer.Length);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        operationToken.ThrowIfCancellationRequested();
        _finished = true;
        // XmlReader supplies no token; use the token of the enclosing document operation.
        var token = cancellationToken.CanBeCanceled ? cancellationToken : operationToken;
        int read = await source.ReadAsync(buffer[..ReadSize(buffer.Length)], token).ConfigureAwait(false);
        return Account(read, buffer.Length);
    }

    internal async Task DrainAsync(byte[]? scratch = null)
    {
        if (_finished)
        {
            return;
        }
        scratch ??= new byte[8192];
        while (await ReadAsync(scratch.AsMemory(), operationToken).ConfigureAwait(false) != 0)
        {
            // Validate the remaining compressed input without retaining its contents.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            source.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
