# DataTransfer 常用流程

导入导出复用现有 Excel 组件：ExcelHelper 负责模型映射、校验与报告，CsvHelper 负责 CSV 解析和编码。`ExcelHelper.ImportCsvWithReportAsync` 与 `ExportCsvAsync` 将二者连接起来，Excel 和 CSV 可以使用同一份模型及行校验函数。

## 类库接入

```csharp
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;

await using var input = File.OpenRead("products.csv");
var helper = new ExcelHelper();
var report = await helper.ImportCsvWithReportAsync<Product>(input,
    row => row.Quantity < 0
        ? [new ExcelRowValidationError("数量不能为负数。", nameof(Product.Quantity))]
        : [], cancellationToken: cancellationToken);

if (!report.IsValid)
{
    await using var errors = new FileStream("errors.xlsx", FileMode.CreateNew, FileAccess.Write);
    await helper.ExportAsync(report.Errors, "Errors", errors, cancellationToken);
    return;
}

// 此处已通过整份校验，可将数据交给应用服务；示例计算总数量。
var products = report.Rows.Select(row => row.Value).ToArray();
var totalQuantity = products.Sum(row => (long)row.Quantity);
await using var output = new FileStream("products-export.csv", FileMode.CreateNew, FileAccess.Write);
await helper.ExportCsvAsync(products, output, cancellationToken: cancellationToken);

public sealed class Product
{
    [ExcelColumn("编号", Required = true, Order = 0)] public int Id { get; set; }
    [ExcelColumn("名称", Required = true, Order = 1)] public string Name { get; set; } = "";
    [ExcelColumn("数量", Required = true, Order = 2)] public int Quantity { get; set; }
}
```

Excel 输入使用 `new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1)`，调用已有 `ImportWithReportAsync<Product>(fileName, input, validateRow, cancellationToken)`，并在 `finally` 中释放 `helper.Workbook`。两种格式都返回 `ExcelImportReport<T>`，可以使用同一段业务处理逻辑；Excel 结果导出使用已有 `ExportAsync`。

报告可能同时包含有效行和错误，示例在 `IsValid` 为 false 时停止业务处理。需要写数据库的应用在校验后自行执行应用服务及事务。导入组件不接管业务写入、回滚或重试。

## CSV 模型映射

| 项目 | 行为 |
| --- | --- |
| 表头 | 第一条逻辑记录为表头，按列名映射，允许重排；表头前后空白被裁剪，比较区分大小写 |
| 模型 | 复用 `ExcelColumn` 的 Name、Order、Ignore、Required 和现有类型转换；使用公开可读写属性与无参构造函数 |
| 列错误 | 缺必填列、重复映射列、不可写属性返回报告；未知列被忽略 |
| 行错误 | 必填空值、转换失败、业务校验错误保留源位置；有错误的行不进入 Rows |
| 业务校验 | 仅对完成字段转换的行调用；回调异常直接传播 |
| 位置 | RowNumber 是从 1 开始的逻辑记录号，包含表头；引号内换行不增加记录号；SheetName 固定为 `CSV` |
| 结构错误 | 空文件、字段数量与表头不一致、CSV 语法错误会失败；语法和字段数量错误带记录、列位置 |
| 数据记录上限 | `maximumRows` 默认 10000，不含表头，包含错误行；超限失败，不返回截断结果 |
| 编码与分隔符 | 复用 `CsvOptions`；默认严格 UTF-8、逗号、输出无 BOM，可显式选择 BOM 与分隔符 |
| 导出 | 按模型列顺序输出表头，使用 invariant 数值格式，DateTime 输出 `O` 格式；null 输出空字段 |
| 资源 | CSV 从流当前位置读取，输入输出均保持打开；报告保存在内存中；Excel 工作簿由调用方释放 |

CSV 导出保留字段文本，引用与转义沿用 [CsvHelper](../../src/MS.Microservice.Excel/docs/csv-design.md) 的语义，不额外转换可能被电子表格识别为公式的文本。

## Lab 完整流程

登录 Lab 后，在 `/api/lab/tools/spreadsheets` 下操作：

1. `POST excel/export` 或 `POST csv/export`，提交 `{}` 下载默认商品示例，也可提交 `Rows` 导出自己的数据。
2. 使用 multipart 字段 `File` 上传到 `excel/import-report` 或 `csv/import-report`，查看校验报告。
3. 上传到 `excel/import` 或 `csv/import`，通过校验后返回 `productCount`、`totalQuantity` 和 `totalValue`。默认示例结果为 2、5、668.70。
4. 存在校验错误时，第 3 步返回 422 和报告，不计算部分商品汇总。上传到对应的 `import-errors` 可下载包含行列位置的 XLSX 错误文件。

示例业务处理是商品汇总，不持久化数据。两种格式共用商品模型和非负数量／单价校验，上传上限沿用 20 MiB。CSV 默认最多 1000 条数据记录，可通过 `MaximumRows` 调整至 10000；`csv/read` 的 MaximumRows 仍包含表头，因为该接口读取原始记录。

返回的导出结果可直接下载。需要保存在本地或 S3 时，将结果流按 [Files 流程](files.md)传给现有 IFileStorage；无需新增存储或任务系统。
