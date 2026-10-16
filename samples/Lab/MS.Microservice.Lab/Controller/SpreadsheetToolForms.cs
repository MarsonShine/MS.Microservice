using System.ComponentModel.DataAnnotations;
using MS.Microservice.Infrastructure.Utils.Excel;

namespace MS.Microservice.Lab.Controller;

/// <summary>整份导入校验通过后计算的商品汇总，不持久化业务数据。</summary>
public sealed record SpreadsheetImportSummary(int ProductCount, long TotalQuantity, decimal TotalValue);

/// <summary>商品工作表示例行；Excel 列名使用中文表头。</summary>
public sealed class SpreadsheetDemoRow
{
    /// <summary>商品编号，对应必填列“编号”。</summary>
    [ExcelColumn("编号", Required = true, Order = 0)] public int Id { get; set; }
    /// <summary>商品名称，对应必填列“名称”。</summary>
    [ExcelColumn("名称", Required = true, Order = 1)] public string Name { get; set; } = "";
    /// <summary>非负数量，对应必填列“数量”。</summary>
    [ExcelColumn("数量", Required = true, Order = 2)] public int Quantity { get; set; }
    /// <summary>非负单价，对应必填列“单价”。</summary>
    [ExcelColumn("单价", Required = true, Order = 3)] public decimal UnitPrice { get; set; }
}

/// <summary>将商品行导出为 XLSX。</summary>
public sealed class SpreadsheetExportRequest
{
    /// <summary>工作表名称，默认“商品”，最多 31 个字符。</summary>
    [Required, StringLength(31)] public string SheetName { get; set; } = "商品";
    /// <summary>导出的商品行，最多 10000 行；省略时使用两条示例商品。</summary>
    [Required, MaxLength(10000)] public List<SpreadsheetDemoRow> Rows { get; set; } =
        [new() { Id = 1, Name = "键盘", Quantity = 2, UnitPrice = 199.50m }, new() { Id = 2, Name = "鼠标", Quantity = 3, UnitPrice = 89.90m }];
}

/// <summary>导入商品 XLS／XLSX，并返回逐行报告。</summary>
public sealed class SpreadsheetImportForm
{
    /// <summary>必填 XLS／XLSX 文件，可先下载 excel/sample 示例，单文件最多 20 MiB。</summary>
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>工作表索引，从 0 开始，默认 0。</summary>
    [Range(0, int.MaxValue)] public int SheetIndex { get; set; }
    /// <summary>表头行索引，从 0 开始，默认 0。</summary>
    [Range(0, int.MaxValue)] public int HeaderRowIndex { get; set; }
    /// <summary>数据起始行索引，从 0 开始，默认 1。</summary>
    [Range(0, int.MaxValue)] public int FirstDataRowIndex { get; set; } = 1;
}

/// <summary>按 UTF-8 读取 CSV 记录。</summary>
public sealed class CsvReadForm
{
    /// <summary>必填 UTF-8 CSV 文件，单文件最多 20 MiB。</summary>
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>单字符分隔符，默认逗号；例如分号“;”。</summary>
    [Required(AllowEmptyStrings = true), StringLength(1, MinimumLength = 1), DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Delimiter { get; set; } = ",";
    /// <summary>记录上限，默认 1000，超限报错。csv/read 包含表头，csv/import 系列只计数据记录。</summary>
    [Range(1, 10000)] public int MaximumRows { get; set; } = 1000;
}

/// <summary>将文本字段写为可下载 CSV。</summary>
public sealed class CsvWriteRequest
{
    /// <summary>记录及字段，最多 10000 条；默认包含表头和一条中文示例。</summary>
    [Required, MaxLength(10000)] public List<string?[]> Rows { get; set; } = [["编号", "名称"], ["1", "示例商品"]];
    /// <summary>单字符分隔符，默认逗号。</summary>
    [Required(AllowEmptyStrings = true), StringLength(1, MinimumLength = 1)] public string Delimiter { get; set; } = ",";
    /// <summary>记录结束符，默认 CRLF；也支持 LF 或 CR。</summary>
    [Required(AllowEmptyStrings = true), StringLength(2, MinimumLength = 1), RegularExpression(@"\r\n|\r|\n")]
    public string NewLine { get; set; } = "\r\n";
    /// <summary>是否写入 UTF-8 BOM，默认 true，便于 Excel 识别中文。</summary>
    public bool IncludeUtf8Bom { get; set; } = true;
}
