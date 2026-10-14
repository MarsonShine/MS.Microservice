# 文档检索

`MS.Microservice.Search` 是可独立引用的本地持久索引组件，提供文件名／路径检索和中英文正文全文检索。宿主提供文件元数据及已提取的文字；PDF、Word、OCR 和后台任务使用现有组件组合，不进入查询路径。

## 建立索引与查询

```csharp
using MS.Microservice.Search;

using var index = new DocumentSearchIndex(new SearchIndexOptions
{
    DirectoryPath = Path.GetFullPath("data/document-index")
});

index.ApplyBatch([
    SearchChange.Upsert(new SearchDocument(
        Scope: "tenant-42:user-7", DocumentId: "contract-123", Version: 1,
        FileName: "采购合同.pdf", Path: "/contracts/2026/采购合同.pdf",
        Content: "合同编号 A-123，采购设备及服务。", LengthBytes: 2048,
        ModifiedAtUtc: DateTimeOffset.UtcNow))
]);
index.Publish();

// 默认优先返回最多 50 条，HasMore 准确，TotalHits 为 null。
var files = index.Search(new SearchRequest("tenant-42:user-7", "合同"));
var text = index.Search(new SearchRequest("tenant-42:user-7", "采购设备", SearchTarget.Content));

// 需要精确总数时显式选择 Exact。
var counted = index.Search(new SearchRequest("tenant-42:user-7", "合同", CountMode: SearchCountMode.Exact));
```

每个索引目录使用一个长期存活实例；它允许并发查询，串行处理写入和发布，并持有单写者锁。宿主必须传入可信的绝对目录，保护索引文件访问权限。索引保存明文元数据和正文，使用者只能读取经过宿主认证／授权得到的 Scope。

| 查询参数 | 默认值 | 行为 |
|---|---|---|
| `Target` | `FileName` | `FileName`、`Path`、`Content` |
| `Match` | 元数据 `Contains`；正文 `AllTerms` | 元数据支持精确包含／前缀；正文支持所有词项／连续短语 |
| `Sort` | 所有 Target 均为 `Path` | 全局路径排序；正文可显式选择 `Relevance` 相关度 |
| `CountMode` | `Fast` | Fast 省略总数；Exact 返回整个匹配集的精确总数 |
| `Limit` | `50` | 1～200；结果带准确的 `HasMore` |
| `Extension` | 空，不筛选 | 大小写不敏感，可带点；空字符串筛选无扩展名文件 |
| `DirectoryPrefix` | 空，不筛选 | 按目录边界筛选，最多 256 字符 |
| `ModifiedFromUtc` / `ModifiedToUtc` | 空，不筛选 | 包含两端；缺少修改时间的文档不匹配时间筛选 |
| `SnippetLength` | `0` | 0 不读取正文；1～1000 返回原文字开头的最多指定 UTF-16 字符，保持代理对完整 |
| `Cursor` | 空，第一页 | 使用上一页 `NextCursor`；可在分页时改变 Limit、Fast／Exact 和摘要长度 |

查询最多 256 个 UTF-16 字符。文件名／路径按 Unicode NFC 和不变量小写匹配，路径反斜杠转斜杠。正文使用 StandardAnalyzer：英文按词项、中文按单字；连续短语按词项位置匹配。英文超长单词遵循分析器默认 255 字符上限。

路径排序按规范化路径的 UTF-8 字节字典序，再按原始 `DocumentId` 字节序；正文显式选择相关度时采用 BM25，平局按当前快照的 Lucene docID。`NextCursor` 绑定索引身份、当前 `IndexGeneration`、Scope、匹配及排序条件。发布更新后沿用旧游标会抛出 `SearchCursorExpiredException`，应从第一页重新查询。

## 更新、删除和发布

```csharp
index.ApplyBatch([SearchChange.Delete("tenant-42:user-7", "contract-123", version: 2)]);
index.Publish();
```

`Scope + DocumentId` 是稳定身份，Version 必须是宿主分配的递增正整数。低版本更新会被忽略；同版本相同内容可重放，同版本不同内容抛出 `SearchVersionConflictException`。删除保存版本墓碑，防止重启后旧任务重新写回文件；更高版本可以重新创建文档。

`ApplyBatch` 缓冲变化；`Publish` 统一提交所有待发布变化并刷新查询快照。正常关闭会丢弃未发布变化。批量预验证会在写入前拒绝非法参数及版本冲突，但取消或 I/O 故障可能留下部分缓冲写入；宿主可先幂等重放该批再发布，或关闭实例放弃未发布部分。

首次大批导入完成后可显式调用一次 `Optimize`，把数据合并为按路径排序的段并发布。日常更新调用 `Publish` 即可。Fast 首屏在排序段提前停止，在新增未排序段正常收集，保持全局排序和准确 `HasMore`；相关度排序和带游标的页仍需遍历候选。查询不会自行刷新或强制合并。

| 写入参数 | 默认限制 |
|---|---|
| Scope／DocumentId | 各 256 个 UTF-16 字符，有效 Unicode；按原值区分大小写 |
| 文件名／逻辑路径 | 512／4096 个 UTF-16 字符 |
| `MaximumContentCharacters` | 每文档 2,000,000 个 UTF-16 字符 |
| `MaximumBatchSize` | 每批 10,000 项 |
| `MaximumBatchContentCharacters` | 每批累计 16,000,000 个 UTF-16 字符 |
| `WriteBufferMegabytes` | 128 MiB，影响 Lucene 写入缓冲，不是进程内存上限 |

同步接口检查取消令牌，不能强制中断正在进行的 Lucene 提交、合并或文件 I/O。使用本地文件系统；首版没有跨进程写入、多机共享索引或索引文件保密能力。Lucene.NET 依赖版本为 4.8.0-beta00018；目录格式不兼容时会拒绝打开，应从权威源重建。

概念、版本及删除处理、排序与总数策略见[实现设计](docs/search-design.md)。性能测量条件、Fast／Exact 分项与复现命令见[基准说明](../../benchmarks/MS.Microservice.Search.Benchmarks/README.md)。
