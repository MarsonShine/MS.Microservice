using System.Globalization;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using Xunit;
using static MS.Microservice.Lab.Tests.Tools.ToolDemoHttpFiles;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class DocumentToolsApiTests
{
    [Fact]
    public async Task Word_SampleTemplateCanBeFilledThroughMultipartApi()
    {
        using var host = new ToolDemoTestHost();
        var filled = await FilledWordAsync(host.Client);
        using var document = WordprocessingDocument.Open(new MemoryStream(filled), false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var text = document.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("客户甲", text, StringComparison.Ordinal);
        Assert.Contains("上海市", text, StringComparison.Ordinal);
        Assert.DoesNotContain("请输入", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_MergeSelectTextAndActualRasterPreservePageOrder()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        AddFile(form, SimplePdf("PAGE_A"), "Files", "first.pdf", "application/pdf");
        AddFile(form, SimplePdf("PAGE_B"), "Files", "second.pdf", "application/pdf");
        using var response = await host.Client.PostAsync("/api/lab/tools/documents/pdf/merge", form);
        response.EnsureSuccessStatusCode();
        var merged = await response.Content.ReadAsByteArrayAsync();
        using var text = JsonDocument.Parse(await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/text", merged, "merged.pdf", "application/pdf"));
        Assert.Equal(2, text.RootElement.GetArrayLength());
        Assert.Contains("PAGE_A", text.RootElement[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("PAGE_B", text.RootElement[1].GetProperty("text").GetString(), StringComparison.Ordinal);
        var selected = await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/select", merged, "merged.pdf", "application/pdf", new() { ["Pages"] = "2" });
        using var selectedText = JsonDocument.Parse(await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/text", selected, "selected.pdf", "application/pdf"));
        Assert.Equal(1, selectedText.RootElement.GetArrayLength());
        Assert.Contains("PAGE_B", selectedText.RootElement[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        var png = await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/render-page", selected, "selected.pdf", "application/pdf", new() { ["Page"] = "1", ["Dpi"] = "72" });
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(300, bitmap.Width);
        Assert.Equal(200, bitmap.Height);
        Assert.True(bitmap.Pixels.Count(pixel => pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100) > 10);
        var protectedPdf = await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/select", merged, "merged.pdf", "application/pdf",
            new() { ["Pages"] = "1", ["OutputUserPassword"] = "reader", ["OutputOwnerPassword"] = "owner" });
        using var info = JsonDocument.Parse(await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/info", protectedPdf, "protected.pdf", "application/pdf", new() { ["Password"] = "reader" }));
        Assert.True(info.RootElement.GetProperty("isEncrypted").GetBoolean());
        Assert.Equal(1, info.RootElement.GetProperty("pages").GetArrayLength());
    }

    private static async Task<byte[]> FilledWordAsync(HttpClient client)
    {
        var sample = await client.GetByteArrayAsync("/api/lab/tools/documents/word/template-sample");
        return await PostFileAsync(client, "/api/lab/tools/documents/word/fill", sample, "template.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", new() { ["ValuesJson"] = "{\"CustomerName\":\"客户甲\",\"Address\":\"上海市\"}" });
    }

    private static byte[] SimplePdf(string marker)
    {
        var content = $"BT /F1 20 Tf 20 100 Td ({marker}) Tj ET\n";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"];
        using var bytes = new MemoryStream();
        void Write(string value) => bytes.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(bytes.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = bytes.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return bytes.ToArray();
    }
}
