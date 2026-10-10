using MS.Microservice.Documents.Internal;

namespace MS.Microservice.Documents.Rendering;

/// <summary>Converts supported trusted DOCX documents on Windows using an isolated LibreOffice process job.</summary>
public sealed class WordPdfConverter
{
    private readonly WordPdfHostOptions _office;

    public WordPdfConverter(WordPdfHostOptions office)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DOCX conversion currently requires Windows process-job supervision.");
        ArgumentNullException.ThrowIfNull(office);
        if (!Path.IsPathFullyQualified(office.LibreOfficeExecutablePath) || !File.Exists(office.LibreOfficeExecutablePath)
            || !Path.IsPathFullyQualified(office.TemporaryDirectory) || string.IsNullOrWhiteSpace(office.DotNetHostPath))
            throw new ArgumentException("Configure an existing absolute LibreOffice executable, a dotnet host, and an absolute temporary directory.", nameof(office));
        _office = office with { TemporaryDirectory = Path.GetFullPath(office.TemporaryDirectory) };
    }

    /// <summary>Reads from the current position and validates a completed PDF before copying; caller streams stay open.</summary>
    public async Task<WordPdfResult> ConvertAsync(Stream docx, Stream destination, WordPdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docx);
        ArgumentNullException.ThrowIfNull(destination);
        if (!docx.CanRead || !destination.CanWrite || ReferenceEquals(docx, destination))
            throw new ArgumentException("Provide distinct readable input and writable output streams.");
        options = Validate(options ?? new());
        cancellationToken.ThrowIfCancellationRequested();
        var source = await ReadAsync(docx, options.Limits.MaximumInputBytes, "DOCX input", cancellationToken).ConfigureAwait(false);
        var (pdf, pages) = await RenderWorkerSupervisor.RunAsync(new WordWorkerRequest(source, _office, options), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await destination.WriteAsync(pdf, cancellationToken).ConfigureAwait(false);
        return new(pages, pdf.Length);
    }

    internal static WordPdfOptions Validate(WordPdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.Limits);
        var limits = options.Limits;
        if (limits.MaximumInputBytes < 1 || limits.MaximumEntries < 1 || limits.MaximumExpandedBytes < 1 || limits.MaximumXmlCharactersPerPart < 1 || limits.MaximumImagePixels < 1
            || limits.MaximumPdfBytes is < 1 or > int.MaxValue - 4 || limits.MaximumPages is < 1 or > 10_000
            || options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentException("DOCX conversion limits and timeout must be positive and within supported bounds.", nameof(options));
        return options;
    }

    internal static async Task<byte[]> ReadAsync(Stream input, int maximum, string format, CancellationToken cancellationToken = default)
    {
        using var output = new BoundedBuffer(maximum, format);
        var buffer = new byte[(int)Math.Min(81920L, (long)maximum + 1)];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, (long)maximum - output.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
            output.Write(buffer, 0, read);
        return output.ToArray();
    }
}
