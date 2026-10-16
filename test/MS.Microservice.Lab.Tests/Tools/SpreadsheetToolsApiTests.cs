using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Lab.Controller;
using Xunit;
using static MS.Microservice.Lab.Tests.Tools.ToolDemoHttpFiles;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class SpreadsheetToolsApiTests
{
    [Theory]
    [InlineData("excel", "products.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("csv", "products.csv", "text/csv")]
    public async Task ExportedProductsCanBeValidatedAndProcessed(string format, string name, string mime)
    {
        using var host = new ToolDemoTestHost();
        using var export = await host.Client.PostAsJsonAsync($"/api/lab/tools/spreadsheets/{format}/export", new { });
        export.EnsureSuccessStatusCode();
        var bytes = await PostFileAsync(host.Client, $"/api/lab/tools/spreadsheets/{format}/import",
            await export.Content.ReadAsByteArrayAsync(), name, mime);
        var summary = JsonSerializer.Deserialize<SpreadsheetImportSummary>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(2, summary.ProductCount);
        Assert.Equal(5, summary.TotalQuantity);
        Assert.Equal(668.70m, summary.TotalValue);
    }

    [Theory]
    [InlineData("excel", "products.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("csv", "products.csv", "text/csv")]
    public async Task InvalidImportDoesNotProcessValidSubsetAndCanDownloadErrors(string format, string name, string mime)
    {
        using var host = new ToolDemoTestHost();
        using var export = await host.Client.PostAsJsonAsync($"/api/lab/tools/spreadsheets/{format}/export", new
        {
            Rows = new[]
            {
                new { Id = 1, Name = "有效", Quantity = 1, UnitPrice = 10m },
                new { Id = 2, Name = "无效", Quantity = -1, UnitPrice = 10m }
            }
        });
        export.EnsureSuccessStatusCode();
        var source = await export.Content.ReadAsByteArrayAsync();
        using var form = new MultipartFormDataContent();
        AddFile(form, source, "File", name, mime);
        using var result = await host.Client.PostAsync($"/api/lab/tools/spreadsheets/{format}/import", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.StatusCode);
        using var json = JsonDocument.Parse(await result.Content.ReadAsByteArrayAsync());
        Assert.False(json.RootElement.GetProperty("isValid").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("totalValue", out _));
        Assert.Equal(1, json.RootElement.GetProperty("rows").GetArrayLength());
        var error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal(3, error.GetProperty("rowNumber").GetInt32());
        Assert.Equal(3, error.GetProperty("columnNumber").GetInt32());

        var errors = await PostFileAsync(host.Client, $"/api/lab/tools/spreadsheets/{format}/import-errors", source, name, mime);
        using var input = new MemoryStream(errors);
        var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
        try
        {
            var report = helper.ImportWithReport<ErrorRow>("errors.xlsx", input);
            Assert.True(report.IsValid);
            var exportedError = Assert.Single(report.Rows).Value;
            Assert.Equal(3, exportedError.RowNumber);
            Assert.Equal(3, exportedError.ColumnNumber);
            Assert.Equal("Quantity", exportedError.PropertyName);
        }
        finally { helper.Workbook?.Dispose(); }
    }

    public sealed class ErrorRow
    {
        public int RowNumber { get; set; }
        public int? ColumnNumber { get; set; }
        public string PropertyName { get; set; } = "";
    }

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
