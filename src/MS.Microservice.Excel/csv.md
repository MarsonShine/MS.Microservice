# CSV 逐行读写

`CsvHelper` 位于现有 Excel 组件中，提供独立的 CSV 行级流接口，不隐式识别表头、不转换类型，也不改变 `ExcelHelper` 与 `MiniExcelHelper` 的现有行为。

```csharp
using MS.Microservice.Infrastructure.Utils;

await foreach (var row in CsvHelper.ReadAsync(input, cancellationToken: cancellationToken))
{
    Console.WriteLine($"第 {row.RowNumber} 条记录，共 {row.Fields.Count} 列");
}

IEnumerable<IReadOnlyList<string?>> rows = GetRows();
await CsvHelper.WriteAsync(output, rows, cancellationToken: cancellationToken);
```

## 格式与错误

- 默认逗号分隔、双引号包围字段；字段中的双引号以两个双引号转义。仅指定的分隔符参与分列，逗号模式下 TAB 是普通字段字符。
- 读取接受 CRLF、LF、CR 三种记录结束符，字段内部的换行字符保持原样。写入默认使用 CRLF，可通过 `CsvOptions.NewLine` 改为 LF 或 CR。
- 不裁剪空白、跳过空行或填补列数；不同记录可以有不同列数。空输入产生零条记录；空行产生一个空字段；尾部分隔符产生末尾空字段；最终换行不额外产生记录。
- `CsvRow.RowNumber` 是从 1 开始的逻辑记录号，包含空行；带换行字段仍属于一条记录。字段集合只读。
- 未闭合引号、非字段开头出现引号、闭合引号后的多余字符以及记录超限抛出 `CsvFormatException`，提供逻辑记录号和字段号。流 I/O、编码、枚举器和取消异常保留原有类型。
- 写入将 `null` 字段作为空字段；拒绝 `null` 记录或零字段记录，因为 CSV 不能区分零字段记录和一个空字段。表头如有需要，应作为普通的第一条记录显式传入。

## 编码、BOM 与流所有权

默认编码为严格 UTF-8、输出无 BOM。读取使用明确配置的编码，不自动猜测 UTF-16/UTF-32 等编码。输入开头至多消费一个编码标记：编码自带 preamble 时由 `StreamReader` 消费；编码不带 preamble 时跳过一个初始解码后 `U+FEFF`，不会再消费第二个标记。UTF-8/UTF-16 有无输出 BOM 的配置不会改变该规则。若第一字段本来就需要以此字符开头，应以双引号包围该字段；写入器自动引用以 `U+FEFF` 开头的数据，确保往返保留。其他位置的 `U+FEFF` 保留。

通过 `CsvOptions.Encoding` 指定其他编码。编码本身的 preamble 控制输出 BOM，例如 `new UTF8Encoding(true, true)` 或 `Encoding.Unicode`。写入已有的可寻址流时，遵循 `StreamWriter` 的规则：只有位置为零时才输出 BOM。旧编码需要调用方自行注册相应编码提供器。

所有操作从流当前位置开始，不复位、不关闭调用方的流，支持不可寻址流。读取是惰性的，校验和错误发生在枚举期间；内部有预读缓冲，提前停止枚举后不能按底层流的位置直接恢复未消费的 CSV 内容。

## 大文件、限额与取消

读写均按记录推进，不把整个文件转换为 `List` 或 `byte[]`。工作内存与当前记录大小有关；调用方保留已读行或自己将所有输出写入 `MemoryStream` 时，仍会增加自身内存占用。

`MaxRecordChars` 默认 1,048,576，按 CSV 原始 UTF-16 字符数计数，包含分隔符、引号和字段内换行，不包含记录结束符；`MaxColumns` 默认 16,384。两项都必须大于零，读写使用相同规则。限制用于避免单条异常记录无限增长，不表示这些列数是 CSV 标准上限。

取消令牌传入读写 I/O，并在字符处理、记录枚举和字段处理时检查。同步输入枚举器或忽略取消令牌的自定义流无法被强制打断。发生错误或取消时，已经读出的记录仍然有效，输出可能不完整；写入器释放时可能刷新已缓冲内容。已有处理错误或取消异常时保留原异常，输入枚举器与写入器的清理错误不覆盖它；无先前错误时，清理失败正常传播。应用需要先写临时文件或私有对象，全部成功后再发布结果。

本接口的严格语法、原始字段换行、当前位置、记录限额和异步读取契约独立于 MiniExcel 1.42.0 的 CSV 接口。其对应实现可见固定版本的 [CsvReader](https://github.com/mini-software/MiniExcel/blob/70fd8e755dfc8220073e1fe5d1b8b5c3e058e463/src/MiniExcel/Csv/CsvReader.cs) 与 [CsvWriter](https://github.com/mini-software/MiniExcel/blob/70fd8e755dfc8220073e1fe5d1b8b5c3e058e463/src/MiniExcel/Csv/CsvWriter.cs)。
