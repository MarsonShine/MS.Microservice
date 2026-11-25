# 同一个请求键重试时，需要读多少数据？

假设客户端用键 `create-1` 创建了一张订单，服务端保存了 201 和一份 64 KiB 的响应。客户端断线后再用这个键请求，服务端有两种处理方式：请求内容相同，回放第一次的结果；请求内容不同，报告冲突。只有回放用得上那 64 KiB。

## 先找到记录，再决定是否返回正文

旧版 `FindResultAsync` 用作用域哈希和键哈希查一行记录，EF 把所有列读回应用，然后 C# 才比较请求指纹。冲突虽然不会把旧响应发给客户端，应用却已为旧正文分配了数组。

新版仍只查询一次。SQL 除了取出判断结果需要的几列，还按指纹决定正文列的值，大意是 `CASE WHEN RequestHash = @fingerprint THEN Body ELSE NULL END`。在标准大写 SHA-256 记录中，两份不同指纹会使数据库直接返回空值，正文留在库里。这样也避开了“两次独立查询之间，记录被清理或重新占用”的时间窗口。

![一次查询按指纹选择返回正文，应用按序号核对指纹后决定是否回放](assets/lookup-body.svg)

数据库和 C# 的字符串比较规则可能不同，所以应用还会用 `StringComparison.Ordinal` 核对返回的指纹。例如，某条历史记录被手工改成小写，不区分大小写的数据库可能仍会返回正文；应用会判为冲突。只有指纹相同才检查完成标记、状态码和正文。已完成的 204 可以有空数组，意外缺失正文的匹配记录仍会报错。回放响应继续持有自己的正文副本。[查询与事务实现](../src/MS.Microservice.Idempotency.EFCore/EfCoreIdempotencyStore.cs) · [兼容性用例](../test/MS.Microservice.Idempotency.EFCore.Tests/EfCoreIdempotencyStoreTests.cs)

条件投影本身也有成本。第一次试验沿用普通 LINQ 构造这条查询，小响应场景的未命中查询从约 10.5 KB 增至 18.4 KB 托管分配。最终实现复用 EF 的参数化编译查询，作用域、键、请求指纹和取消令牌由每次调用传入。查询委托放在宿主 EF 的内存缓存里，使用提供程序的查询缓存键区分模型和查询配置。跨请求复用的是查询执行方式，每次仍从当前上下文读取数据库。编译查询只适用于匹配的 EF 模型。[EF Core 编译查询说明](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)

## 三个哈希如何与历史记录匹配

`IdempotencyRequest.Create` 产生三个大写十六进制 SHA-256。作用域哈希覆盖操作名的 UTF-8 字节、一个零字节和调用者身份的 UTF-8 字节；另外两个分别覆盖键和调用方传入的原始请求表示。原先要先拼接作用域字符串、创建作用域和键的字节数组，并为三个 32 字节摘要各建一个数组。

现在操作名和键在字符范围校验后写入同一块栈缓冲，先写作用域并做哈希，再从起点写键。每次只取本次写入的有效长度。缓冲上限由输入约束确定：128 字节操作名 + 1 字节分隔符 + 1024 字节身份，共 1153 字节。请求正文直接从传入的 span 计算，三个摘要分别写入定长栈空间，再生成要保存的哈希字符串。原有 UTF-8 编码及大写格式保持兼容，旧数据无需迁移。[指纹实现](../src/MS.Microservice.Idempotency.EFCore/IdempotencyRequest.cs)

## 同机实测

2026-11-25，Windows x64、Intel Core i5-9400、.NET SDK 11.0.100、Release。基线为 Git `ddbe305`。表中每行都是一次公共 API 调用，B 表示进程托管分配字节（四舍五入到整数）。

| `Create` 输入 | 旧耗时 | 新耗时 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 短 ASCII 身份、键和请求 | 1.991 µs | 1.782 µs | 873 | 497 |
| 960 字节 Unicode 身份、128 字节键、短请求 | 8.625 µs | 7.493 µs | 2706 | 498 |
| 短身份和键、1 MiB 请求正文 | 4.349 ms | 4.350 ms | 962 | 578 |

1 MiB 场景仍需对全部正文字节做 SHA-256，耗时基本持平。

| `FindResultAsync`，SQLite 内存库 | 旧 µs/次 | 新 µs/次 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 键不存在（64 B 对照组） | 99.02 | 48.83 | 10145 | 3836 |
| 回放 64 B | 104.58 | 58.10 | 11673 | 4732 |
| 同键冲突，已存 64 B | 103.99 | 53.86 | 11473 | 4444 |
| 键不存在（64 KiB 对照组） | 98.84 | 48.03 | 10145 | 3836 |
| 回放 64 KiB | 133.38 | 83.73 | 142617 | 135676 |
| 同键冲突，已存 64 KiB | 123.59 | 54.83 | 76945 | 4444 |

把已存响应从 64 B 换成 64 KiB 后，新版冲突查询的分配仍约为 4.4 KB。回放则要读取并保存真正的响应，分配仍随正文变大。以上是预热后的应用内耗时，不含首次查询编译、HTTP 传输或数据库网络往返。Npgsql 提供程序也验证了三参数和条件正文列的 SQL 翻译，未连接 PostgreSQL 测量执行耗时。

## 复现

从仓库根目录运行。对照旧版时，将此基准的项目文件和 `Program.cs` 复制到 `ddbe305` 检出目录的相同位置，用同一 SDK 分别构建。

```powershell
$bench = 'MS.Microservice.Idempotency/benchmarks/MS.Microservice.Idempotency.Benchmarks'
dotnet build $bench -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --no-build --project $bench -c Release
dotnet test MS.Microservice.Idempotency/MS.Microservice.Idempotency.slnx -c Release
```

两个版本各启动三个进程，顺序为旧新、新旧、旧新。每个进程对各场景预热后测七轮，先取轮次中位数，再取三个进程的中位数。用 `Stopwatch` 计时，以 `GC.GetTotalAllocatedBytes(true)` 差值计算托管分配，未测进程峰值内存。查找场景复用同一个数据库连接和上下文，写入和事务提交位于计时外。驱动在计时前核对哈希、查询分支、状态码、内容类型、Location 和完整响应字节。[基准源码](../benchmarks/MS.Microservice.Idempotency.Benchmarks/Program.cs)
