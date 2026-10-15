using System.Text;
using MS.Microservice.Documents.Pdf;
using MS.Microservice.Documents.Word;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Search;
using MS.Microservice.Tasks;

namespace MS.Microservice.Lab.Application.Tools;

internal sealed record DocumentIndexPayload(string Owner, string FileKey, string DocumentId, long Version,
    string FileName, string LogicalPath, bool UseOcr);

internal sealed class DocumentIndexJobHandler(DemoFileStore files, DocumentSearchIndex index,
    ToolInferenceRuntime inference) : IJobHandler<DocumentIndexPayload>
{
    internal static readonly string[] Extensions = [".txt", ".md", ".json", ".csv", ".pdf", ".docx", ".png", ".jpg", ".jpeg", ".webp"];
    private const int MaximumTextCharacters = 2_000_000;

    public async Task<string?> ExecuteAsync(DocumentIndexPayload payload, JobExecutionContext context, CancellationToken cancellationToken)
    {
        var storage = files.ForOwner(payload.Owner);
        var metadata = await storage.GetMetadataAsync(payload.FileKey, cancellationToken)
            ?? throw new FileNotFoundException("索引源文件已删除。");
        if (metadata.Length > ToolDemoFiles.MaximumFileBytes) throw new InvalidDataException("索引源文件最多 20 MiB。");
        await context.ReportProgressAsync(5, "读取文件");
        await using var source = await storage.OpenReadAsync(payload.FileKey, cancellationToken);
        var bytes = await ToolDemoFiles.ReadBoundedAsync(source, ToolDemoFiles.MaximumFileBytes, cancellationToken);
        using var input = new MemoryStream(bytes, writable: false);
        await context.ReportProgressAsync(20, "提取正文");
        var extension = Path.GetExtension(payload.FileKey).ToLowerInvariant();
        var text = extension switch
        {
            ".docx" => await new WordTextExtractor().ExtractAsync(input, new()
            {
                MaximumInputBytes = ToolDemoFiles.MaximumFileBytes,
                MaximumTextCharacters = MaximumTextCharacters,
                MaximumXmlCharactersPerPart = 4_000_000
            }, cancellationToken),
            ".pdf" => await ExtractPdfAsync(input, payload.UseOcr, context, cancellationToken),
            ".png" or ".jpg" or ".jpeg" or ".webp" => payload.UseOcr
                ? await RecognizeAsync(input, cancellationToken)
                : throw new InvalidDataException("图片正文索引需要启用 useOcr，并准备本地 OCR 环境。"),
            ".txt" or ".md" or ".json" or ".csv" => await ReadTextAsync(input, cancellationToken),
            _ => throw new NotSupportedException("不支持此索引源文件格式。")
        };
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("未提取到正文；扫描 PDF 可启用 useOcr。状态失败时不会发布空正文索引。");
        if (text.Length > MaximumTextCharacters) throw new InvalidDataException("提取正文最多 200 万字符。");
        cancellationToken.ThrowIfCancellationRequested();
        await context.ReportProgressAsync(90, "发布索引");
        index.ApplyBatch([SearchChange.Upsert(new(context.Scope, payload.DocumentId, payload.Version,
            payload.FileName, payload.LogicalPath, text, bytes.Length, metadata.LastModifiedUtc))], cancellationToken);
        index.Publish(cancellationToken);
        return $"search:{payload.DocumentId}:{payload.Version}";
    }

    private static async Task<string> ReadTextAsync(Stream input, CancellationToken token)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var text = await reader.ReadToEndAsync(token);
        return text.TrimStart('\uFEFF');
    }

    private Task<string> RecognizeAsync(Stream input, CancellationToken token) => inference.RunAsync(async innerToken =>
    {
        var result = await new OcrProcessor(inference.OcrOptions()).RecognizeAsync(input, innerToken);
        return string.Join('\n', result.Regions.Select(region => region.Text));
    }, token);

    private async Task<string> ExtractPdfAsync(MemoryStream input, bool useOcr, JobExecutionContext context, CancellationToken token)
    {
        var options = new PdfOptions { Limits = new()
        {
            MaximumInputBytes = ToolDemoFiles.MaximumFileBytes, MaximumPages = 50,
            MaximumRasterDimension = 4096, MaximumRasterPixels = 4_000_000,
            MaximumOutputBytes = 8_000_000
        } };
        var processor = new PdfProcessor();
        var pages = await processor.ExtractTextAsync(input, options, token);
        var text = new StringBuilder();
        foreach (var page in pages)
        {
            var pageText = page.Text;
            if (useOcr && string.IsNullOrWhiteSpace(pageText))
            {
                input.Position = 0;
                using var image = new MemoryStream();
                await processor.RenderPageAsync(input, page.PageNumber, image, 100, options, token);
                image.Position = 0;
                pageText = await RecognizeAsync(image, token);
            }
            if ((long)text.Length + pageText.Length + 1 > MaximumTextCharacters)
                throw new InvalidDataException("提取正文最多 200 万字符。");
            text.AppendLine(pageText);
            await context.ReportProgressAsync(20 + 60 * page.PageNumber / Math.Max(pages.Count, 1), "提取 PDF 正文");
        }
        return text.ToString();
    }
}
