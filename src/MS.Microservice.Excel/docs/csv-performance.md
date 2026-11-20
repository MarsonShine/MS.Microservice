# 从一行 CSV 看异步写入与缓冲复用

假设一行有五个字段，依次是 `订单`、`x,y`、`say "hi"`、含换行的 `a\nb` 和 `null`。中间三个要加外层引号，`hi` 两边的双引号还要各写两遍。下面的换行位于字段内部。

```csv
订单,"x,y","say ""hi""","a
b",
```

## 一行为什么曾经需要 16 次异步写入？

基线 `39a704c` 在 `CsvHelper.WriteAsync` 的列循环里分别写分隔符、字段及引号。第一个普通字段调用一次 `StreamWriter.WriteAsync`，随后三个带引号字段各调用四次，最后的空字段调用两次（分隔符和空正文），行结束符再调用一次。合计 `1 + 3 × 4 + 2 + 1 = 16` 次进入写入器。底层流可能合并这些写入，文件系统真正落盘的次数要在另一层测量。

![同一行 CSV 的 16 次片段写入合并成一次 30 字符的块写入](assets/csv-write-block.svg)

```csharp
// 旧版，已判定需要加引号的字段
await writer.WriteAsync("\"".AsMemory(), cancellationToken).ConfigureAwait(false);
await writer.WriteAsync(value.Replace("\"", "\"\"", StringComparison.Ordinal).AsMemory(), cancellationToken).ConfigureAwait(false);
await writer.WriteAsync("\"".AsMemory(), cancellationToken).ConfigureAwait(false);
```

新版从数组池租用工作区，每批最多暂存 4096 个字符。分隔符、正文和引号依次放进数组，`used` 记录实际填入的长度。该行连同行结束符都能装下，整行只提交一次。以前 `Replace` 为含内嵌引号的字段生成过渡字符串，现在遇到 `"` 就把它连续放入两个位置。每次提交都等待写入完成，再覆盖数组开头。归还前会清空本次写过的全部位置，包括遇到异常时仍在暂存的前缀。[写入循环](../CsvHelper.cs#L211) · [转义](../CsvHelper.cs#L235) · [清理](../CsvHelper.cs#L283) · [提交有效前缀](../CsvHelper.cs#L289)

```csharp
block[used++] = c;
if (c == '"') block[used++] = c;
```

如果字段本身很长，会根据剩余空间提前提交。需要转义时，一次处理至多 2048 个源字符。即使这 2048 个全是引号，翻倍后仍然能放进 4096 个位置。无需翻倍的超长正文可以直接交给写入器。这里的 4096 是 UTF-16 字符容量，编码后的输出字节数取决于 `CsvOptions.Encoding`。[超长字段的直接写入](../CsvHelper.cs#L218) · [边界与尾块的逐字节测试](../../../test/MS.Microservice.Excel.Tests/CsvHelperTests.cs#L287)

判断记录是否合法仍在写字段之前进行。`a"b` 的值长 3，外层两个引号加 2，内嵌引号重复一次加 1，因此它在记录预算里占 6。换行符在引号内计入预算，在记录结尾不计入。字符检查按固定大小的片段推进并检查取消，读取器原有的跨块状态、逻辑行列和编码规则见 [CSV 语法与生命周期](csv-design.md)。

## ExportCsvAsync 的每行数组去了哪里？

模型导出另有一层迭代器 `Records()`。原来的 `meta.Slots.Select(...).ToArray()` 会为表头和每条数据行创建新的字段数组。在 256 条记录、2 列的例子里，共有 257 个数组用来存放这些引用。现在先建一份与列数等长的数组，第一次填表头，以后每次按槽位重填模型值。[模型导出](../ExcelHelper.Csv.cs#L87)

```csharp
var fields = new string?[meta.Slots.Length];
for (int i = 0; i < fields.Length; i++) fields[i] = meta.Slots[i].ColumnName;
yield return fields;
```

这份数组只交给同一次调用内部的 CSV 写入器。它读完并写完上一行才向迭代器请求下一行，调用方也拿不到这些行，因而可以覆盖同一个数组。数值、日期生成文本的工作仍然存在。输入仍按需枚举，失败与取消仍会释放枚举器并让调用者保有输出流。对外返回的 `CsvRow.Fields` 则可以被调用者保存，读取端会为各行保留独立字段集合。

基准分别测一个普通字段、256 行混合字段、长引号字段、长普通字段及类型化模型。每批完成后逐字节检查末次操作的整个 CSV。输入与复测口径见[基准源码](../../../benchmarks/MS.Microservice.Excel.Benchmarks/CsvCases.cs)和[验收记录](../../../docs/validation/EVENTSOURCING-EXCEL-PERF/summary.md)。
