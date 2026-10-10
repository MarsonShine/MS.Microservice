# 文本差异、编码转换与 JSON 排版

这些工具属于现有 Core，无需新增项目或第三方依赖。目录标题规范化继续使用 `Common.TextNormalizer`，字段脱敏继续使用 `Security.SecretField`；本页的操作不自动清洗、合并空白或脱敏。

## 逐行差异

```csharp
var changes = TextDiff.Compare(original, updated);
foreach (var line in changes)
{
    // line.Kind: Unchanged、Added、Removed
    // line.Text 保留原行的换行符。
}
```

比较使用精确、区分大小写的行内容，保留空白、Unicode、CR、LF、CRLF 与末行有无换行的区别。空字符串没有行；以换行结束时不会再增加虚构的空行。替换表示为删除与新增，多个等长最优结果中选择一个确定的结果，不保证模拟特定编辑器的显示顺序。

过滤 `Added` 后拼接 `Text` 可以还原原文；过滤 `Removed` 后拼接可以还原新文。算法选择最长公共子序列，使用 Hirschberg 分治和复用的两行工作向量，附加内存随行数线性增长，不建立完整的二维矩阵。

相同的开头和结尾先直接输出。剩余两侧行数为 N、M 时，最多按 `2 × N × M` 的 LCS 比较预算检查，默认 `maximumComparisonWork = 4_000_000`；超过时抛出 `ArgumentException`，调用方可明确提高预算。该限制控制行比较次数，不限制单行字符长度、输入字符串大小或结果大小，宿主应另外控制输入长度。

## 流式编码转换

```csharp
await TextEncodingConverter.ConvertAsync(
    input, output,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
    new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
    cancellationToken);
```

转换从输入流当前位置开始，写到输出流当前位置，使用固定大小字符缓冲区并支持不可定位、分段读取的输入。输入输出都由调用方释放。同一个流实例不能同时作为输入和输出；包装同一底层文件或流的不同实例也不应由调用方用于原地转换。

输入严格采用调用方提供的 `Encoding`，不根据 BOM 切换编码，其解码及目标编码的回退策略均保留。传入严格编码时，无效输入或无法表示的输出会抛出对应的 fallback 异常；默认替换回退则按照 `Encoding` 的策略替换。

BOM 遵循标准 reader/writer 行为：

- 输入开头匹配 `sourceEncoding.GetPreamble()` 的字节会被消费。若该编码未配置 preamble，相同字节将按正文解码，例如 UTF-8 BOM 可能保留为 U+FEFF。
- 输出 BOM 由 `targetEncoding.GetPreamble()` 决定。可定位输出在非零位置写入时不会插入 BOM；不可定位输出无法检查位置，会按目标编码写 preamble。空输入也可能输出目标 BOM。

取消、读写错误和编码错误直接抛出，不关闭调用方流。失败时输出可能已有部分内容，清理 writer 时也可能刷新缓冲内容；清理再次失败不会覆盖最初的错误或取消。调用方必须丢弃失败的输出，需完整成功后发布时可组合临时文件保存。

## JSON 排版

```csharp
var formatted = JsonText.Format(json);                 // 缩进
var compact = JsonText.Format(json, indented: false);  // 紧凑
```

`Serialization.JsonText` 使用 `System.Text.Json` DOM 读取一个完整 JSON 值，保留属性顺序、名称、重复属性、数组顺序、字符串值和原始数字 token。字符串转义形式及排版空白可能变化；不将大数字转换为浮点数，不调用应用对象转换器或 `DefaultSerializeSetting` 的命名策略。

非法 JSON、注释、尾逗号、多值输入或超过标准 DOM 默认深度的内容抛出 `JsonException`。空字符串无效，JSON 的 `null` 有效。此 API 会在内存中保留文档和结果，宿主应限制输入大小。
