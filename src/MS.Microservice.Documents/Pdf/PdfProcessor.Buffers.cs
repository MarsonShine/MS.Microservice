using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Text;

namespace MS.Microservice.Documents.Pdf;

public sealed partial class PdfProcessor
{
    private static PdfOptions Settings(PdfOptions? options)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Limits);
        var limits = options.Limits;
        if (limits.MaximumInputBytes < 1 || limits.MaximumMergedInputBytes < 1 || limits.MaximumOutputBytes < 1 || limits.MaximumPages < 1
            || limits.MaximumRasterDimension < 1 || limits.MaximumRasterPixels < 1)
            throw new ArgumentException("PDF limits must be positive.", nameof(options));
        if ((options.OutputUserPassword is null) != (options.OutputOwnerPassword is null))
            throw new ArgumentException("Provide both output user and owner passwords.", nameof(options));
        if (options.OutputUserPassword is not null)
        {
            ValidatePassword(options.OutputUserPassword);
            ValidatePassword(options.OutputOwnerPassword!);
        }
        return options;
    }

    private static void ValidatePassword(string password)
    {
        var utf8 = new UTF8Encoding(false, true);
        if (password.Length == 0 || utf8.GetByteCount(password) > 127
            || utf8.GetByteCount(password.Normalize(NormalizationForm.FormKC)) > 127)
            throw new ArgumentException("PDF output passwords must contain 1 to 127 UTF-8 bytes, including after normalization.");
    }

    private static UglyToad.PdfPig.PdfDocument OpenContent(byte[] bytes, string? password, out string? resolvedPassword)
    {
        resolvedPassword = password;
        try { return UglyToad.PdfPig.PdfDocument.Open(bytes, new UglyToad.PdfPig.ParsingOptions { Password = password ?? string.Empty }); }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException) when (password is not null)
        {
            // Older encryption may depend on exact password bytes; normalize only after refusal.
            var normalized = password.Normalize(NormalizationForm.FormKC);
            if (normalized == password) throw;
            resolvedPassword = normalized;
            return UglyToad.PdfPig.PdfDocument.Open(bytes, new UglyToad.PdfPig.ParsingOptions { Password = normalized });
        }
    }

    private static void CheckPages(int count, PdfLimits limits)
    {
        if (count < 1 || count > limits.MaximumPages)
            throw new InvalidDataException("The PDF page count exceeds the configured range.");
    }

    private static int[] Selection(IReadOnlyList<int>? pages, int count, PdfLimits limits)
    {
        CheckPages(count, limits);
        if (pages is null) return Enumerable.Range(1, count).ToArray();
        CheckPages(pages.Count, limits);
        var selected = pages.ToArray();
        if (selected.Any(page => page < 1 || page > count)) throw new ArgumentOutOfRangeException(nameof(pages));
        return selected;
    }

    private static void Destination(Stream output, params Stream[] inputs)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Output must be writable.", nameof(output));
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (ReferenceEquals(input, output)) throw new ArgumentException("Input and output must be distinct streams.");
        }
    }

    private static async Task<byte[]> ReadAsync(
        Stream input, PdfLimits limits, CancellationToken cancellationToken, long? remainingMergeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input must be readable.", nameof(input));
        cancellationToken.ThrowIfCancellationRequested();
        var maximumBytes = (int)Math.Min(limits.MaximumInputBytes, remainingMergeBytes ?? limits.MaximumInputBytes);
        using var data = new BoundedBuffer(maximumBytes);
        var buffer = new byte[(int)Math.Min(81920L, (long)maximumBytes + 1)];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, (long)maximumBytes - data.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
            data.Write(buffer, 0, read);
        cancellationToken.ThrowIfCancellationRequested();
        return data.ToArray();
    }

    private static byte[] Save(PdfDocument document, PdfOptions options, bool sourceEncrypted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceEncrypted && options.OutputUserPassword is null && !options.AllowUnencryptedOutput)
            throw new InvalidOperationException("Encrypted PDF inputs require output passwords or explicit AllowUnencryptedOutput.");
        if (options.OutputUserPassword is not null)
        {
            document.SecurityHandler.SetEncryptionToV5();
            document.SecuritySettings.UserPassword = options.OutputUserPassword;
            document.SecuritySettings.OwnerPassword = options.OutputOwnerPassword!;
        }
        using var output = new BoundedBuffer(options.Limits.MaximumOutputBytes);
        var expectedPages = document.PageCount;
        document.Save(output, closeStream: false);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = output.ToArray();
        if (options.OutputUserPassword is not null)
        {
            var requiresPassword = false;
            try { using var plaintext = OpenContent(bytes, null, out _); }
            catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException) { requiresPassword = true; }
            if (!requiresPassword)
                throw new ArgumentException("Output passwords must prevent passwordless access after PDF normalization.");
            foreach (var password in new[] { options.OutputUserPassword, options.OutputOwnerPassword })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var verified = OpenContent(bytes, password, out _);
                    if (!verified.IsEncrypted || verified.NumberOfPages != expectedPages)
                        throw new InvalidDataException("The protected PDF output failed verification.");
                }
                catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException error)
                {
                    throw new ArgumentException("The output password is incompatible with the PDF reading backends.", nameof(options), error);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }

    private sealed class OwnedPdf : IDisposable
    {
        private readonly MemoryStream _input;
        public PdfDocument Document { get; }
        public bool IsEncrypted { get; }
        public OwnedPdf(byte[] bytes, PdfOptions options, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Import)
        {
            _input = new MemoryStream(bytes, writable: false);
            PdfDocument? document = null;
            try
            {
                string? password;
                using (var content = OpenContent(bytes, options.Password, out password))
                    IsEncrypted = content.IsEncrypted;
                document = PdfReader.Open(_input, password, mode);
                CheckPages(document.PageCount, options.Limits);
                Document = document;
            }
            catch { document?.Dispose(); _input.Dispose(); throw; }
        }
        public void Dispose()
        {
            try { Document.Dispose(); }
            finally { _input.Dispose(); }
        }
    }

    private sealed class BoundedBuffer(int maximumBytes) : MemoryStream
    {
        private void Reserve(int count)
        {
            if (count > maximumBytes - Position) throw new InvalidDataException("PDF data exceeds the configured byte limit.");
            var required = (int)(Position + count);
            if (required > Capacity)
                Capacity = (int)Math.Min(maximumBytes, Math.Max(required, Math.Max(256L, Capacity * 2L)));
        }
        public override void Write(byte[] buffer, int offset, int count) { Reserve(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Reserve(1); base.WriteByte(value); }
        public override void SetLength(long value)
        {
            if (value > maximumBytes) throw new InvalidDataException("PDF data exceeds the configured byte limit.");
            if (value > Capacity)
                Capacity = (int)Math.Min(maximumBytes, Math.Max(value, Math.Max(256L, Capacity * 2L)));
            base.SetLength(value);
        }
    }
}
