using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static MS.Microservice.Lab.Tests.Tools.ToolDemoHttpFiles;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class SpreadsheetToolsApiTests
{
    [Fact]
    public async Task Spreadsheet_SampleWorkbookCanBeImportedWithLocatedRows()
    {
        using var host = new ToolDemoTestHost();
        var sample = await host.Client.GetByteArrayAsync("/api/lab/tools/spreadsheets/excel/sample");
        var bytes = await PostFileAsync(host.Client, "/api/lab/tools/spreadsheets/excel/import-report", sample, "products.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var json = JsonDocument.Parse(bytes);
        Assert.True(json.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("errors").GetArrayLength());
        var rows = json.RootElement.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(2, rows[0].GetProperty("rowNumber").GetInt32());
        Assert.Equal("键盘", rows[0].GetProperty("value").GetProperty("name").GetString());
        Assert.Equal(199.50m, rows[0].GetProperty("value").GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task Csv_WriteAndReadPreserveChineseAndQuotedDelimiter()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/spreadsheets/csv/write", new
        { Rows = new[] { new[] { "编号", "名称" }, new[] { "1", "商品;中文" } }, Delimiter = ";", IncludeUtf8Bom = true });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var csv = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, csv[..3]);
        var bytes = await PostFileAsync(host.Client, "/api/lab/tools/spreadsheets/csv/read", csv, "records.csv", "text/csv", new() { ["Delimiter"] = ";" });
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal(1, json.RootElement[0].GetProperty("rowNumber").GetInt32());
        Assert.Equal("编号", json.RootElement[0].GetProperty("fields")[0].GetString());
        Assert.Equal("商品;中文", json.RootElement[1].GetProperty("fields")[1].GetString());
    }

    [Theory]
    [InlineData("\r\n", ",")]
    [InlineData("\n", ";")]
    [InlineData("\r", "|")]
    [InlineData("\r\n", " ")]
    [InlineData("\n", "\t")]
    public async Task Csv_ValidWhitespaceSyntaxSurvivesJsonAndFormBinding(string newline, string delimiter)
    {
        using var host = new ToolDemoTestHost();
        var value = "中文" + delimiter + "字段\n第二行\"引号\"";
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/spreadsheets/csv/write", new
        { Rows = new[] { new[] { value, "" }, new[] { "结尾", " 保留空白 " } }, Delimiter = delimiter, NewLine = newline, IncludeUtf8Bom = false });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var csv = await response.Content.ReadAsByteArrayAsync();
        Assert.EndsWith(newline, System.Text.Encoding.UTF8.GetString(csv), StringComparison.Ordinal);
        var bytes = await PostFileAsync(host.Client, "/api/lab/tools/spreadsheets/csv/read", csv, "records.csv", "text/csv", new() { ["Delimiter"] = delimiter });
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal(value, json.RootElement[0].GetProperty("fields")[0].GetString());
        Assert.Equal("", json.RootElement[0].GetProperty("fields")[1].GetString());
        Assert.Equal(" 保留空白 ", json.RootElement[1].GetProperty("fields")[1].GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n\r")]
    [InlineData("\r\n\r")]
    public async Task Csv_InvalidRecordTerminatorsRemainRejected(string? newline)
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/spreadsheets/csv/write", new { NewLine = newline });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
