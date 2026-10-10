# Excel 与 CSV

现有 `ExcelHelper` 提供 XLS/XLSX 导入导出和模板填充，`MiniExcelHelper` 提供另一套表格接入，`DynamicExcelBuilder` 写入动态模板内容。具体模型映射沿用 `ExcelColumnAttribute`。流接口并不代表工作簿逐行解码；NPOI 仍在内存中维护工作簿。

`CsvHelper` 提供严格、带记录限额的异步 CSV 行级读写，支持明确编码、分隔符、字段内换行、取消与调用方流所有权；详见 [CSV 逐行读写](csv.md)。CSV 不隐式识别表头或映射业务模型。

`ExcelHelper.ImportWithReport` / `ImportWithReportAsync` 收集必填、转换、模板映射与业务校验错误，保留原始行列并复用现有导出器生成错误报告；详见[导入报告](import-reports.md)。旧导入保留宽松映射行为，导入及模板打开均保留调用方输入流，工作簿仍需调用方释放。
