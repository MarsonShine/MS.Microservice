# CSV 实现与边界

调用及参数见 [Excel README](../README.md#csv)，实现为 [CsvHelper](../CsvHelper.cs)和[CsvOptions](../CsvOptions.cs)。

读取使用显式编码的 `StreamReader` 和固定字符缓冲，状态为字段起点、未引用、引用中、闭合引号后。只在非引用状态识别记录结束，CRLF 的 LF 单独跳过，因此字段内换行与跨缓冲引号可以原样保留。

引号必须位于字段开头，双引号转义为单个字段字符；闭合引号后只能跟分隔符或记录结束。未闭合引用和非法状态转换失败。空输入无记录；空行有一个空字段；尾分隔符产生尾空字段；最终换行不虚构额外记录。字段集合只读，逻辑记录号包括空记录。

原始记录预算在语法解析前累计，计入引号、转义、分隔符与字段内换行，不计记录终止符。写入预估引用与转义开销，采用同一限额语义；零字段记录因无法与单空字段区分而拒绝。限额是资源策略，不是 CSV 标准列数限制。

## 编码标记

关闭 BOM 编码猜测后，reader 仍消费配置编码自带的 preamble。仅当该编码没有 preamble 时，组件再跳过一个初始解码 `U+FEFF`，避免两层重复消费。引用字段的开头不是初始 BOM 位置；writer 主动引用以 `U+FEFF` 开始的数据，保证往返保留。

writer 的编码决定输出 preamble。可定位流在非零位置写入不重复插入，旧编码提供器由宿主注册。不同编码标记不触发自动切换。

## 生命周期

读取惰性枚举，在枚举阶段验证参数与内容；reader 预读不能作为恢复解析的字节游标。调用方流始终保留打开。

写入显式管理源枚举器和 writer。从枚举器获取、推进、记录处理、编码到刷新均可能失败。已有错误或取消时，分别尝试清理枚举器与 writer，不让清理错误覆盖原错误；正常路径的清理错误传播。清理可能刷新部分输出，因此发布必须由调用者控制。同步枚举器与不响应令牌的流不能强行取消。

本接口不复用 MiniExcel 的宽松 CSV 映射，因为需要固定语法、原始换行、记录预算和异步生命周期。参照版本为 MiniExcel 1.42.0 的 [CsvReader](https://github.com/mini-software/MiniExcel/blob/70fd8e755dfc8220073e1fe5d1b8b5c3e058e463/src/MiniExcel/Csv/CsvReader.cs)及 [CsvWriter](https://github.com/mini-software/MiniExcel/blob/70fd8e755dfc8220073e1fe5d1b8b5c3e058e463/src/MiniExcel/Csv/CsvWriter.cs)。

验证见 [CsvHelperTests](../../../test/MS.Microservice.Excel.Tests/CsvHelperTests.cs)，覆盖语法、BOM 双层处理、原始换行、分段流、预算边界、取消和源／writer 清理双重失败。它们验证 CSV 记录接口，不证明工作簿逐行解码能力。
