using System.Net;
using System.Net.Http.Json;
using System.Text;
using MS.Microservice.Core.Text;
using static MS.Microservice.Lab.Tests.Tools.CoreToolTestRequests;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class TextToolsApiTests
{
    [Fact]
    public async Task JsonFormattingReturnsJsonWithPreservedNumbersAndDuplicateProperties()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/text/json", new
        {
            json = "{ \"number\" : 1.2300, \"same\" : 1, \"same\" : 2, \"text\" : \"中文\" }", indented = false
        });

        await Status(response, HttpStatusCode.OK);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", text);
        Assert.Contains("1.2300", text);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(4, document.RootElement.EnumerateObject().Count());
        Assert.Equal("中文", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task TextComparisonRetainsExactNewlinesAndDescribesReplacement()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/text/compare",
            new { original = "same\r\nold\n", updated = "same\r\nnew\n" });
        var lines = await Json<TextDiffLine[]>(response);

        Assert.Contains(lines, line => line.Kind == TextDiffKind.Unchanged && line.Text == "same\r\n");
        Assert.Contains(lines, line => line.Kind == TextDiffKind.Removed && line.Text == "old\n");
        Assert.Contains(lines, line => line.Kind == TextDiffKind.Added && line.Text == "new\n");
    }

    [Theory]
    [InlineData("", "added\n", TextDiffKind.Added)]
    [InlineData("removed\n", "", TextDiffKind.Removed)]
    public async Task EmptyTextIsAValidSideOfAnActualComparison(string original, string updated, TextDiffKind kind)
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/text/compare", new { original, updated });

        Assert.Equal(kind, Assert.Single(await Json<TextDiffLine[]>(response)).Kind);
    }

    [Fact]
    public async Task EncodingConvertsUtf8BomUnicodeToUtf16Download()
    {
        using var host = new ToolDemoTestHost();
        const string text = "中文和 emoji ✓😀\r\n第二行";
        var encoding = new UTF8Encoding(true, true);
        using var form = Form("File", "source.txt", [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
        form.Add(new StringContent("true"), "SourceBom");

        using var response = await host.Client.PostAsync("/api/lab/tools/text/encoding", form);

        await Status(response, HttpStatusCode.OK);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        var result = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xff, 0xfe }, result[..2]);
        Assert.Equal(text, Encoding.Unicode.GetString(result, 2, result.Length - 2));
    }
}
