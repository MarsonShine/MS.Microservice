# 文档检索 Fast／Exact 对照

此基准调用生产 `DocumentSearchIndex`，分别测量相同匹配与排序的 Fast 和 Exact。默认建立百万条文件元数据、十万份合成正文，固定种子 20261014。组件所有 Target 默认全局路径排序；基准为正文相关度案例显式指定 BM25，另含正文按默认路径排序场景。

```powershell
dotnet run -c Release --project benchmarks/MS.Microservice.Search.Benchmarks -- --output artifacts/search-benchmark/results-fast-exact.json
```

每个场景先校验 Fast／Exact 返回身份一致、精确总数符合源数据、HasMore 正确，再预热 8 次、采样 60 次。报告 P50/P95/P99、每查询分配、工作集、索引字节、导入和显式 Optimize 耗时。首次导入只优化一次；结果文件和索引目录写入被忽略的 artifacts。

可指定 `--metadata`、`--content`、`--samples`、`--index` 和 `--output`，`--case` 可只运行一个命名场景。`--reuse-index` 在已有的同规模、同数据、同版本索引上只生成参考结果并查询，不重新导入或优化；不匹配的计数会失败。不要并行运行多个大档。

命中量包含高频单字、双字、中文短语、补充平面汉字、共同路径、唯一值、缺失值、前缀及 Scope 过滤。全部主数据位于同一个认证 Scope，另加 100 条其他 Scope 数据，避免以租户分片无意缩小百万条查询规模。正文原文平均约 753 个 UTF-16 字符，内容高度重复，不能代表真实长文档或真实压缩率。

Fast 返回前 50 条及准确 HasMore，TotalHits=null；路径排序的排序段可在 K+1 提前停止。Exact 返回相同前 50 条并精确计算整个匹配集总数。正文相关度 Fast 仍需遍历候选，不把它描述为路径早停。

测量包括参数验证、词项分析、过滤、收集、排序、结果字段读取、可选 160 字符摘要、游标序列化及分配。没有 HTTP、OCR、文件解析或高亮。使用持久 FSDirectory 和热 OS 缓存，查询单线程，无并发写入；P99 仅来自固定 60 次采样。

此前精确计数原型在 i5-9400 / .NET 10.0.13 / Windows 10 的百万元数据中，路径包含 `/2025/` 的 P95 为 46.82ms、单字符 `a` 为 36.61ms；正文中文短语 P95 28.63ms。该原型没有 Fast 策略，正文同样是平均 753 字符的合成短文档；新结果应单独报告，不能追认旧 Exact 已通过 10ms 目标。当前运行的 JSON 是实际结果依据。

## 2026-10-14 目标档结果

Windows 10.0.19045 x64、.NET 10.0.13、Intel i5-9400 2.90GHz／6 逻辑处理器。机器未锁定背景负载，运行期间有其他 dotnet 工作；不据此断言具体尾延迟原因。

主 Scope 为 1,000,000 条元数据，其中 100,000 条带正文，另有 100 条其他 Scope 数据。正文累计 75,291,699 个 UTF-16 字符。导入及首次发布 117.69s，唯一一次 Optimize 158.70s；索引 348,900,338 字节（约 332.74MiB），1 个段全部排序。整个进程峰值工作集 1,461,624,832 字节（约 1.36GiB），包含构建和合并阶段。

| 场景／排序 | 命中数 | Fast P95 ms | Exact P95 ms |
|---|---:|---:|---:|
| 名称 report／Path | 83,402 | 3.22 | 69.36 |
| 名称 a／Path | 416,958 | 4.52 | 165.46 |
| 名称 财务报告／Path | 83,347 | 1.94 | 20.80 |
| 路径包含 /2025/／Path | 250,000 | 1.99 | 76.20 |
| 路径前缀／Path | 125,000 | 7.46 | 197.56 |
| 唯一名称片段／Path | 1 | 7.45 | 6.48 |
| 正文 文／Path（默认策略） | 100,000 | 4.53 | 21.53 |
| 正文 检索性能 短语／Path（定点追加测量） | 100,000 | 4.93 | 34.83 |
| 正文 document／Relevance | 100,000 | 17.60 | 19.78 |
| 正文 检索性能 短语／Relevance | 100,000 | 61.80 | 49.50 |

已测的 11 个元数据 Fast 首屏场景 P95 为 0.25～7.46ms；这不保证未优化的大索引、更多段、后续页或真实语料具有相同延迟。正文默认路径排序测了表中的单字和中文短语案例。可选 Relevance 中文短语 Fast 超过 50ms，因此相关度策略尚未全面达到正文目标。Exact 高命中场景仍明显超过 10ms，原有性能限制保留。

原始 JSON 默认位于 `artifacts/search-benchmark/results-fast-exact.json`，包含每场景 P50/P95/P99、分配、工作集、排序及总数语义。一次对照测量只支持这些条件下的结论，不代表全面达到 Everything 的性能水平。

中文短语 Path 行为复用同一个已建排序索引进行的独立追加测量，未重新导入或 Optimize，也未重复其他场景。两种策略均为 10 万命中，并校验相同前 50 条及 HasMore；Fast P50/P95/P99 为 4.04/4.93/5.97ms，Exact 为 33.44/34.83/35.73ms。其结果位于 `artifacts/search-benchmark/results-han-phrase-path.json`，不与原单字 Path 数据混用。

```powershell
$benchmarkReport = Get-Content artifacts/search-benchmark/results-fast-exact.json -Raw | ConvertFrom-Json
dotnet run -c Release --project benchmarks/MS.Microservice.Search.Benchmarks -- --reuse-index --index $benchmarkReport.IndexPath --metadata $benchmarkReport.MetadataDocuments --content $benchmarkReport.ContentDocuments --case body-han-phrase-path --output artifacts/search-benchmark/results-han-phrase-path.json
```
