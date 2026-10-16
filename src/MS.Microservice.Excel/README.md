# Excel 与 CSV

`ExcelHelper` 提供 XLS/XLSX 导入、导出、模板填充和逐行错误报告；`CsvHelper` 提供有记录限额的异步 CSV 读写。`MiniExcelHelper` 使用 MiniExcel 自身的模型映射，不能混用 `ExcelColumnAttribute` 与 MiniExcel 的映射特性。

| 能力 | 入口 | 设计说明 |
| --- | --- | --- |
| 工作簿导入／导出 | `ExcelHelper.Import` / `Export` 及异步重载 | [导入设计](docs/import-reports-design.md) |
| 带校验的导入 | `ImportWithReport` / `ImportWithReportAsync` | [报告与映射](docs/import-reports-design.md) |
| 模板填充 | `OpenExcel`、`DynamicExcelBuilder` | 公共接口注释 |
| CSV 记录读写 | `CsvHelper.ReadAsync` / `WriteAsync` | [CSV 设计](docs/csv-design.md) |
| CSV 模型导入／导出 | `ExcelHelper.ImportCsvWithReportAsync` / `ExportCsvAsync` | 复用 Excel 列映射、校验和错误报告；[DataTransfer 流程](../../docs/components/data-transfer.md) |

## 导入与错误报告

```csharp
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;

await using var input = File.OpenRead("orders.xlsx");
var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
try
{
    var report = await helper.ImportWithReportAsync<OrderRow>("orders.xlsx", input,
        row => row.Quantity < 0
            ? [new ExcelRowValidationError("数量不能为负数", nameof(OrderRow.Quantity))]
            : [], cancellationToken);

    foreach (var row in report.Rows)
        Console.WriteLine($"Excel 第 {row.RowNumber} 行：{row.Value.Id}");

    if (!report.IsValid)
    {
        await using var errors = new FileStream("errors.xlsx", FileMode.CreateNew, FileAccess.Write);
        new ExcelHelper().Export(report.Errors, "Errors", errors);
    }
}
finally { helper.Workbook?.Dispose(); }

public sealed class OrderRow
{
    [ExcelColumn("编号", Required = true)] public int Id { get; set; }
    [ExcelColumn("数量", Required = true)] public int Quantity { get; set; }
}
```

| 参数／结果 | 规则 |
| --- | --- |
| 工作表 | `InitSheetIndex` 从 0 开始，或用 `InitSheetName`；未指定时沿用最后一个工作表 |
| `InitStartReadRowIndex` | 标题和内容起点都从 0 开始，必须初始化 |
| 模型 | 公开无参构造函数、公开可读属性；报告导入的映射属性还须可写，字段不映射 |
| `ExcelColumn.Required` | 报告入口检查源列／源值；0 和 false 有效 |
| 业务回调 | 仅接收单元格有效的行，返回消息及可选属性名 |
| `Rows` | 通过校验的行，附原始一基 Excel 行号 |
| `Errors` | 工作表、行列、属性、错误类型、消息及原始值，可用已有 `Export` 导出 |

报告有错误时仍可能包含其他有效行，业务自行决定是否部分接收。非必填空值保留默认值，已有物理空行参与校验。

旧 `Import` 保持宽松行为：无法转换的值可能保留默认值，不强制 `Required`。ExcelHelper 的列名比较区分大小写；读取、导出及模板实例不应并发复用。调用方负责释放工作簿（模板由 `builder.Workbook` 提供），输入输出流保持打开。

**NPOI 会将工作簿加载到内存；Stream／Async 接口不等于逐行流式解码。** 不可定位异步输入还会先缓冲。取消在缓冲及报告行处理阶段检查，不能中途打断 NPOI 同步打开；宿主必须限制工作簿大小。`ExcelHelper` 模型映射依赖运行时生成的属性访问器，不适用于 Native AOT／裁剪。

## CSV

```csharp
using MS.Microservice.Infrastructure.Utils;

await using var csvInput = File.OpenRead("input.csv");
await foreach (var row in CsvHelper.ReadAsync(csvInput, cancellationToken: cancellationToken))
    Console.WriteLine($"记录 {row.RowNumber}：{row.Fields.Count} 列");

IReadOnlyList<string?>[] rows = [["Id", "Name"], ["1", "示例"]];
await using var csvOutput = new FileStream("output.csv", FileMode.CreateNew, FileAccess.Write);
await CsvHelper.WriteAsync(csvOutput, rows,
    new CsvOptions { Delimiter = ';' }, cancellationToken);
```

| `CsvOptions` | 默认／约束 |
| --- | --- |
| `Delimiter` | 逗号；禁止引号、换行、NUL、BOM 及代理项 |
| `NewLine` | 写入 CRLF；可选 LF 或 CR，读取接受三者 |
| `Encoding` | 严格 UTF-8，无输出 BOM；显式编码，不自动猜测 |
| `MaxRecordChars` | 1,048,576；原始 UTF-16 字符计数，包含引号／分隔符／字段内换行，不含记录结束符 |
| `MaxColumns` | 16,384；两项限额都必须为正 |

CSV 使用双引号引用字段，字段内双引号以两个双引号转义，字段内换行保留。不自动识别表头、转换模型、裁剪空白或统一列数。空行是一条含空字段的记录；`RowNumber` 为一基逻辑记录号，字段内换行不增加记录号。写入将 null 字段视为空，拒绝 null 或零字段记录。语法和限额错误抛出带记录／字段位置的 `CsvFormatException`。

CSV 按记录推进，不加载完整文件；工作内存取决于当前记录。流从当前位置处理并保持打开。编码及输出 BOM 由 `Encoding` 配置决定。

读写或取消失败可能留下部分输出，调用者应丢弃失败输出或在完整成功后再发布。具体语法与异常边界见[CSV 设计](docs/csv-design.md)。
