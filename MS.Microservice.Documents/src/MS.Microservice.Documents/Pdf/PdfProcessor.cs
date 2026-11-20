using System.Runtime.CompilerServices;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MS.Microservice.Documents.Pdf;

public sealed partial class PdfProcessor
{
    public async Task<PdfDocumentInfo> InspectAsync(Stream input, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = Settings(options);
        using var owned = new OwnedPdf(await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false), options);
        var document = owned.Document;
        var pages = document.Pages.Cast<PdfPage>().Select((page, index) =>
            new PdfPageInfo(index + 1, page.Width.Point, page.Height.Point, (page.Rotate % 360 + 360) % 360)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(document.Info.Title, document.Info.Author, owned.IsEncrypted, pages);
    }

    public async Task SelectPagesAsync(
        Stream input, Stream output, IReadOnlyList<int> pages, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        Destination(output, input);
        ArgumentNullException.ThrowIfNull(pages);
        options = Settings(options);
        using var source = new OwnedPdf(await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false), options);
        var selected = Selection(pages, source.Document.PageCount, options.Limits);
        using var result = new PdfDocument();
        CopyMetadata(source.Document, result);
        foreach (var page in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.AddPage(source.Document.Pages[page - 1]);
        }
        await output.WriteAsync(Save(result, options, source.IsEncrypted, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task MergeAsync(
        IEnumerable<Stream> inputs, Stream output, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Destination(output);
        options = Settings(options);
        var sources = new List<OwnedPdf>();
        var encodedBytes = 0L;
        var sourceEncrypted = false;
        using var result = new PdfDocument();
        try
        {
            foreach (var input in inputs)
            {
                Destination(output, input);
                var bytes = await ReadAsync(input, options.Limits, cancellationToken,
                    options.Limits.MaximumMergedInputBytes - encodedBytes).ConfigureAwait(false);
                encodedBytes = checked(encodedBytes + bytes.Length);
                var source = new OwnedPdf(bytes, options);
                sources.Add(source);
                sourceEncrypted |= source.IsEncrypted;
                CheckPages(checked(result.PageCount + source.Document.PageCount), options.Limits);
                if (result.PageCount == 0) CopyMetadata(source.Document, result);
                foreach (var page in source.Document.Pages.Cast<PdfPage>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.AddPage(page);
                }
            }
            CheckPages(result.PageCount, options.Limits);
            await output.WriteAsync(Save(result, options, sourceEncrypted, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { foreach (var source in sources) source.Dispose(); }
    }

    public async IAsyncEnumerable<PdfPart> SplitAsync(
        Stream input, PdfOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = Settings(options);
        using var source = new OwnedPdf(await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false), options);
        for (var page = 0; page < source.Document.PageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var part = new PdfDocument();
            CopyMetadata(source.Document, part);
            part.AddPage(source.Document.Pages[page]);
            yield return new(page + 1, Save(part, options, source.IsEncrypted, cancellationToken));
        }
    }

    public async Task<IReadOnlyList<PdfPageText>> ExtractTextAsync(
        Stream input, PdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = Settings(options);
        var bytes = await ReadAsync(input, options.Limits, cancellationToken).ConfigureAwait(false);
        using var document = OpenContent(bytes, options.Password, out _);
        CheckPages(document.NumberOfPages, options.Limits);
        var pages = new List<PdfPageText>();
        long outputBytes = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(page);
            outputBytes += Encoding.UTF8.GetByteCount(text);
            if (outputBytes > options.Limits.MaximumOutputBytes) throw new InvalidDataException("Extracted text exceeds the output byte limit.");
            pages.Add(new(page.Number, text));
        }
        return pages;
    }

    private static void CopyMetadata(PdfDocument source, PdfDocument destination)
    {
        destination.Info.Title = source.Info.Title;
        destination.Info.Author = source.Info.Author;
        destination.Info.Subject = source.Info.Subject;
        destination.Info.Keywords = source.Info.Keywords;
    }
}
