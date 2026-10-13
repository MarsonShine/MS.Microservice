using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using MS.Microservice.Lab.Controller;
using Xunit;
using static MS.Microservice.Lab.Tests.Tools.ToolDemoHttpFiles;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class RenderingToolsApiTests
{
    [LabHtmlRenderingFact]
    public async Task Rendering_InstalledBrowserProducesTwoPageChinesePdf()
    {
        var configuration = new Dictionary<string, string?>();
        if (Environment.GetEnvironmentVariable("HTML_RENDERING_BROWSER_PATH") is { Length: > 0 } browser)
            configuration["ToolDemos:HtmlExecutablePath"] = browser;
        using var host = new ToolDemoTestHost(configuration);
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/rendering/html-pdf", new
        { Html = "<meta charset='utf-8'><p style='break-after:page'>接口中文报告</p><p>第二页</p>" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var text = JsonDocument.Parse(await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/text",
            await response.Content.ReadAsByteArrayAsync(), "report.pdf", "application/pdf"));
        Assert.Equal(2, text.RootElement.GetArrayLength());
        Assert.Contains("接口中文报告", text.RootElement[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("第二页", text.RootElement[1].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [LabWordRenderingFact]
    public async Task Rendering_ConfiguredLibreOfficeConvertsFilledWordToChinesePdf()
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?> { ["ToolDemos:LibreOfficeExecutablePath"] = Environment.GetEnvironmentVariable("LIBREOFFICE_TEST_PATH") });
        var docx = await FilledWordAsync(host.Client);
        var pdf = await PostFileAsync(host.Client, "/api/lab/tools/rendering/docx-pdf", docx, "filled.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        using var text = JsonDocument.Parse(await PostFileAsync(host.Client, "/api/lab/tools/documents/pdf/text", pdf, "document.pdf", "application/pdf"));
        Assert.Equal(1, text.RootElement.GetArrayLength());
        Assert.Contains("客户甲", text.RootElement[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("上海市", text.RootElement[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WidthMillimeters", 1000d)]
    [InlineData("HeightMillimeters", 1000d)]
    [InlineData("MarginMillimeters", 499d)]
    public void FloatingFormRangesRespectFractionalUpperBounds(string property, double maximum)
    {
        object model = new HtmlPdfDemoRequest();
        var context = new ValidationContext(model) { MemberName = property };
        object invalid = maximum + 0.4;
        object valid = maximum;
        Assert.False(Validator.TryValidateProperty(invalid, context, new List<ValidationResult>()));
        Assert.True(Validator.TryValidateProperty(valid, context, new List<ValidationResult>()));
    }

    private static async Task<byte[]> FilledWordAsync(HttpClient client)
    {
        var sample = await client.GetByteArrayAsync("/api/lab/tools/documents/word/template-sample");
        return await PostFileAsync(client, "/api/lab/tools/documents/word/fill", sample, "template.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", new() { ["ValuesJson"] = "{\"CustomerName\":\"客户甲\",\"Address\":\"上海市\"}" });
    }
}

public sealed class LabHtmlRenderingFactAttribute : FactAttribute
{
    public LabHtmlRenderingFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("RUN_LAB_TOOL_NATIVE_TESTS") != "true")
            Skip = "Set RUN_LAB_TOOL_NATIVE_TESTS=true on Windows to run installed browser rendering.";
    }
}

public sealed class LabWordRenderingFactAttribute : FactAttribute
{
    public LabWordRenderingFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("RUN_LAB_TOOL_NATIVE_TESTS") != "true"
            || !File.Exists(Environment.GetEnvironmentVariable("LIBREOFFICE_TEST_PATH")))
            Skip = "Set RUN_LAB_TOOL_NATIVE_TESTS=true and LIBREOFFICE_TEST_PATH on Windows to run actual LibreOffice conversion.";
    }
}
