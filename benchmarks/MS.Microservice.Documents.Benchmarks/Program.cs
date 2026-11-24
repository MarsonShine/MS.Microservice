using MS.Microservice.Documents.Pdf;
using MS.Microservice.Documents.Rendering;
using MS.Microservice.Documents.Word;
using PdfDocument = UglyToad.PdfPig.PdfDocument;

byte[] photo = Fixtures.Photo();
if (args is ["--render"])
{
    // The consumer runs on Windows with Microsoft Edge installed. The worker starts a fresh browser per call.
    var renderer = new HtmlPdfRenderer(new HtmlBrowserOptions { Channel = HtmlBrowserChannel.MicrosoftEdge });
    var options = new HtmlPdfOptions
    {
        Assets = new Dictionary<string, HtmlPdfAsset> { ["photo.jpg"] = new("image/jpeg", photo) }
    };
    const string html = "<h1>Fixture</h1><img src='photo.jpg' width='300' height='300'>";
    using var output = new MemoryStream();
    MeasuredOperation.Run("Edge / one page with embedded photo", 1, () =>
    {
        output.SetLength(0);
        return renderer.RenderAsync(html, output, options).GetAwaiter().GetResult();
    }, result =>
    {
        output.Position = 0;
        using var pdf = PdfDocument.Open(output);
        var page = pdf.GetPage(1);
        return result.Bytes == output.Length && result.Pages == 1 && pdf.NumberOfPages == 1
            && page.Text.Contains("Fixture", StringComparison.Ordinal) && page.NumberOfImages == 1;
    });
    Console.WriteLine($"Rendered PDF: {output.Length} B; JPEG: {photo.Length} B");
    return;
}

var extractor = new WordTextExtractor();
var longText = string.Concat(Enumerable.Repeat("Report 正文 🐈 ", 65_536));
foreach (var (headers, title, iterations) in new[] { (0, "Report", 24), (12, "Report", 24), (0, longText, 2) })
{
    var (zip, expected) = Fixtures.WordSections(headers, title);
    using var input = new MemoryStream(zip, writable: false);
    MeasuredOperation.Run($"DOCX / extract {title.Length} body chars and {headers} headers", iterations, () =>
    {
        input.Position = 0;
        return extractor.ExtractAsync(input).GetAwaiter().GetResult();
    }, text => text == expected);
}

var processor = new WordTemplateProcessor();
var values = new Dictionary<string, string?> { ["name"] = "已完成" };
foreach (var (media, original, iterations) in new[] { (Array.Empty<byte>(), "", 8), (photo, "", 1), (Array.Empty<byte>(), longText, 1) })
{
    var template = Fixtures.WordTemplate(media, original);
    string expected = "已完成" + original;
    using var input = new MemoryStream(template, writable: false);
    using var output = new MemoryStream();
    MeasuredOperation.Run($"DOCX / fill with {media.Length} image bytes and {original.Length} body chars", iterations, () =>
    {
        input.Position = 0;
        output.SetLength(0);
        return processor.FillAsync(input, output, values).GetAwaiter().GetResult();
    }, result => result.FilledControls == 1 && Fixtures.VerifyTemplate(output, media, expected));
}

var pdfProcessor = new PdfProcessor();
foreach (byte[] image in new[] { Array.Empty<byte>(), photo })
{
    var inputBytes = Fixtures.Pdf(image);
    using var input = new MemoryStream(inputBytes, writable: false);
    MeasuredOperation.Run($"PDF / inspect {inputBytes.Length} bytes", image.Length == 0 ? 48 : 8, () =>
    {
        input.Position = 0;
        return pdfProcessor.InspectAsync(input).GetAwaiter().GetResult();
    }, info => !info.IsEncrypted && info.Pages.Count == 1 && info.Pages[0] is { WidthPoints: 360, HeightPoints: 480 });
    if (image.Length == 0)
    {
        MeasuredOperation.Run("PDF / inspect forward-only short reads", 48, () =>
        {
            using var shortReads = new ShortReadInput(inputBytes);
            return pdfProcessor.InspectAsync(shortReads).GetAwaiter().GetResult();
        }, info => !info.IsEncrypted && info.Pages.Count == 1 && info.Pages[0] is { WidthPoints: 360, HeightPoints: 480 });
    }
    using var output = new MemoryStream();
    MeasuredOperation.Run($"PDF / select {inputBytes.Length} bytes", image.Length == 0 ? 32 : 4, () =>
    {
        input.Position = 0;
        output.SetLength(0);
        pdfProcessor.SelectPagesAsync(input, output, [1]).GetAwaiter().GetResult();
        return output;
    }, stream =>
    {
        stream.Position = 0;
        using var pdf = PdfDocument.Open(stream);
        var page = pdf.GetPage(1);
        return pdf.NumberOfPages == 1 && page.Text == "Fixture" && (image.Length == 0
            ? page.NumberOfImages == 0 : page.GetImages().Single().RawMemory.Span.SequenceEqual(image));
    });
}

internal sealed class ShortReadInput(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(buffer.Length, 17)], cancellationToken);
}
