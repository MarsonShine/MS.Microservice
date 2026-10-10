# 文本工具设计

使用示例与参数见 [Core README](../README.md#文本与-json)。实现分别为 [TextDiff](../Text/TextDiff.cs)、[TextEncodingConverter](../Text/TextEncodingConverter.cs)和[JsonText](../Serialization/JsonText.cs)。

## 行差异

按精确字符串比较保留终止符的行，不规范化大小写、空白或 Unicode。空字符串没有行，末尾换行不会虚构额外行；替换表现为删除与新增。去掉 `Added` 后拼接可还原原文，去掉 `Removed` 后可还原新文。

先去除相同首尾，再用 Hirschberg 分治求最长公共子序列，复用两行工作向量，不建立二维比较矩阵。剩余两边行数为 N、M 时按 `2 × N × M` 检查工作预算；它限制比较工作，不限制单行字符、输入或结果占用。多个最优序列选择确定结果，但不承诺与特定编辑器相同的显示顺序。

## 编码与 BOM

转换使用 4096 字符缓冲和显式编码的 reader／writer，关闭自动编码检测，保留编码自身回退策略。匹配源 `GetPreamble()` 的字节由 reader 消费；源没有 preamble 时，这些字节可能成为正文 `U+FEFF`。输出 BOM 由目标 preamble 决定，可定位输出非零位置不重复插入；不可定位输出按 writer 行为写入，空输入也可能产生 BOM。

取消、编码或 I/O 失败可留下部分输出；writer 清理可能继续刷新缓冲。已有错误时清理故障不替换原错误，无先前错误时清理失败正常传播。组件不关闭调用者流，也不能通过比较实例检测不同包装流是否共享同一底层文件，调用者不得原地转换。

## JSON

`JsonDocument` 读取一个完整值，再通过 `Utf8JsonWriter` 写出。保留属性名称、顺序、重复属性、数组顺序和原始数字 token；字符串转义及空白可能变化。它不调用业务对象转换器或应用序列化命名策略。

默认 DOM 深度规则适用。注释、尾逗号、多值及非法输入失败；空字符串无效，JSON `null` 有效。文档及结果都在内存中，调用者控制输入上限。

验证覆盖 [行差异](../../../test/MS.Microservice.Core.Tests/Text/TextDiffTests.cs)与[编码转换](../../../test/MS.Microservice.Core.Tests/Text/TextEncodingConverterTests.cs)中的换行、Unicode、预算、BOM、分段流、取消和异常清理。规范化与脱敏继续由各自已有组件承担。
