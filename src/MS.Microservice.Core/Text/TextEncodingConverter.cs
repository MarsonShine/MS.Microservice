using System.Text;

namespace MS.Microservice.Core.Text;

public static class TextEncodingConverter
{
    /// <summary>
    /// Converts remaining input using the supplied encodings and fallback policies, leaving both streams open.
    /// Configured encoding markers are honored; output may be partial on failure or cancellation.
    /// </summary>
    public static async Task ConvertAsync(
        Stream source,
        Stream destination,
        Encoding sourceEncoding,
        Encoding targetEncoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceEncoding);
        ArgumentNullException.ThrowIfNull(targetEncoding);
        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Input and output must be different streams.", nameof(destination));
        if (!source.CanRead)
            throw new ArgumentException("The input stream must be readable.", nameof(source));
        if (!destination.CanWrite)
            throw new ArgumentException("The output stream must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new StreamReader(source, sourceEncoding,
            detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var writer = new StreamWriter(destination, targetEncoding, bufferSize: 4096, leaveOpen: true);
        try
        {
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await writer.DisposeAsync().ConfigureAwait(false); }
            catch { /* Preserve the primary conversion or cancellation failure if cleanup also fails. */ }
            throw;
        }

        await writer.DisposeAsync().ConfigureAwait(false);
    }
}
