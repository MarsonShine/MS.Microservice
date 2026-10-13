using System.Text;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

/// <summary>Excel 商品表导出、导入报告与 CSV 读写示例。</summary>
[LabOnly]
[Route("api/lab/tools/spreadsheets")]
public sealed class SpreadsheetToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>下载可直接再次导入的商品 XLSX 示例。</summary>
    /// <remarks>表头为编号、名称、数量、单价，包含两条正常商品。下载并编辑后使用 excel/import-report；不需要安装 Microsoft Excel。</remarks>
    [HttpGet("excel/sample"), Produces(ExcelMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sample()
    {
        var request = new SpreadsheetExportRequest();
        return Download(await ExportAsync(request.Rows, request.SheetName), ExcelMime, "products-sample.xlsx");
    }

    /// <summary>将 JSON 商品列表导出为 XLSX。</summary>
    /// <remarks>提交 Rows 和工作表名称后下载商品表，可再交给 excel/import-report 读取。不需要 Office 进程；工作簿在内存中处理。</remarks>
    [HttpPost("excel/export"), Consumes("application/json"), Produces(ExcelMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromBody] SpreadsheetExportRequest request)
        => Download(await ExportAsync(request.Rows, request.SheetName), ExcelMime, "products.xlsx");

    /// <summary>导入商品 XLS／XLSX，返回有效行与定位错误报告。</summary>
    /// <remarks>使用 excel/sample 的中文表头。工作表、表头和数据起点索引从 0 开始；返回的源 Excel 行号从 1 开始。数量和单价须非负，导入不写数据库。</remarks>
    [HttpPost("excel/import-report"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType(typeof(ExcelImportReport<SpreadsheetDemoRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportReport([FromForm] SpreadsheetImportForm form) => Ok(await ImportAsync(form));

    /// <summary>将商品表的导入校验报告下载为 XLSX。</summary>
    /// <remarks>上传与 excel/import-report 相同的商品表，下载包含工作表、源行列、错误类型与消息的报告；无错误时下载空报告表。</remarks>
    [HttpPost("excel/import-errors"), Consumes("multipart/form-data"), Produces(ExcelMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportErrors([FromForm] SpreadsheetImportForm form)
    {
        var report = await ImportAsync(form);
        return Download(await ExportAsync(report.Errors, "Errors"), ExcelMime, "import-errors.xlsx");
    }

    /// <summary>读取 UTF-8 CSV，并返回逻辑记录和原始字段。</summary>
    /// <remarks>可先用 csv/write 下载示例再上传。引号、字段内换行和空白会保留，不自动识别表头或转换数值；示例每条最多 256 列、65536 字符。</remarks>
    [HttpPost("csv/read"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<CsvRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadCsv([FromForm] CsvReadForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        var rows = new List<CsvRow>();
        await foreach (var row in CsvHelper.ReadAsync(input, new() { Delimiter = Delimiter(form.Delimiter), MaxColumns = 256, MaxRecordChars = 65536 }, Token))
        {
            if (rows.Count >= form.MaximumRows) throw new InvalidDataException("CSV 记录数超过本次允许返回的数量。");
            rows.Add(row);
        }
        return Ok(rows);
    }

    /// <summary>将 JSON 文本记录导出为 CSV。</summary>
    /// <remarks>Rows 的首条可自行作为表头；字段会按 CSV 规则引用和转义，null 写为空。默认下载带 BOM 的 UTF-8 中文 CSV，可再交给 csv/read 读取。</remarks>
    [HttpPost("csv/write"), Consumes("application/json"), Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> WriteCsv([FromBody] CsvWriteRequest request)
    {
        using var output = new MemoryStream();
        await CsvHelper.WriteAsync(output, request.Rows, new()
        {
            Delimiter = Delimiter(request.Delimiter), NewLine = request.NewLine, Encoding = new UTF8Encoding(request.IncludeUtf8Bom, true),
            MaxColumns = 256, MaxRecordChars = 65536
        }, Token);
        return Download(output.ToArray(), "text/csv; charset=utf-8", "records.csv");
    }

    private async Task<ExcelImportReport<SpreadsheetDemoRow>> ImportAsync(SpreadsheetImportForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token, UploadFileKind.Excel), writable: false);
        var helper = new ExcelHelper().InitSheetIndex(form.SheetIndex).InitStartReadRowIndex(form.HeaderRowIndex, form.FirstDataRowIndex);
        try { return await helper.ImportWithReportAsync<SpreadsheetDemoRow>(Path.GetFileName(form.File.FileName), input, ValidateProduct, Token); }
        finally { helper.Workbook?.Dispose(); }
    }

    private async Task<byte[]> ExportAsync<T>(IReadOnlyList<T> rows, string sheetName)
    {
        var helper = new ExcelHelper();
        using var output = new MemoryStream();
        try { await helper.ExportAsync(rows, sheetName, output, Token); return output.ToArray(); }
        finally { helper.Workbook?.Dispose(); }
    }

    private static IEnumerable<ExcelRowValidationError> ValidateProduct(SpreadsheetDemoRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name)) yield return new("商品名称不能为空。", nameof(row.Name));
        if (row.Quantity < 0) yield return new("数量不能为负数。", nameof(row.Quantity));
        if (row.UnitPrice < 0) yield return new("单价不能为负数。", nameof(row.UnitPrice));
    }

    private static char Delimiter(string value) => value is { Length: 1 } ? value[0] : throw new ArgumentException("分隔符须为单个字符。");
}
