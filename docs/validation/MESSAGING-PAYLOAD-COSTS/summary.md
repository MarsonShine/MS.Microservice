# 一条消息的正文何时需要进入内存

一条 128 KiB 的消息发送失败后，运维页面通常只展示消息 ID、契约、消费方、失败时间和错误码。旧版 SelfManaged 查询死信时，会把正文也读出来。类似的开销也出现在发送端：RabbitMQ 编码器先创建完整的正文字节数组，然后才检查消息能否发送。

## 查询 32 条死信时，应用读了什么

下图使用基准中的一组输入。Outbox 是发件箱，Inbox 是收件箱，两边各有 32 条死信。每条正文是 128 KiB 的 ASCII JSON。

![从两个箱子读取候选记录、合并排序并返回 32 条摘要的新旧路径](dead-letter-query.svg)

旧代码对两张表分别执行 `ToListAsync`，EF Core 需要为 64 个实体还原正文字符串。光是这些正文就有 8 MiB 的 UTF-8 内容，转成 .NET 的 UTF-16 字符后约占 16 MiB。现在，两个查询都在 `ToListAsync` 之前用 `Select` 指定摘要所需的列。应用合并候选项后，只为最终入选的 32 条记录生成回放标识，收件箱标识中的消费方名称仍按 URI 规则转义。[死信列表实现](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/SelfManagedFailedMessageOperations.cs)

每张表仍按完成时间排序、各取最多 `limit` 条；合并时按实际失败时间排序。完成时间为空的历史记录沿用创建或接收时间，同一时刻的发件箱记录仍排在收件箱记录之前。正文留在表内，回放时可以读取。[排序与回放测试](../../../MS.Microservice.Messaging/test/MS.Microservice.Messaging.SelfManaged.Tests/ReplayTests.cs)

在 SQLite 上捕获 `ListAsync(32)` 实际执行的两条 SQL，两条均带 `LIMIT 32`（参数化），各返回六列。出箱查询以 `COALESCE(CompletedAtUtc, CreatedAtUtc)` 生成失败时间，收箱查询回退到 `ReceivedAtUtc`。返回列包括 ID、契约名、版本、消费方、失败时间及错误码。

| 每条正文大小 | 旧查询 ms/次 | 新查询 ms/次 | 旧托管 B/次 | 新托管 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 128 B | 0.957 | 0.619 | 119525 | 81011 |
| 64 KiB | 15.878 | 0.763 | 8495347 | 81011 |
| 128 KiB | 38.940 | 0.911 | 16883270 | 81011 |

## 发送前先确定 UTF-8 字节数

把上限设为 1 MiB。对一条编码后刚好多出 1 字节的正文，旧代码会创建一个 1,048,577 字节的数组，填完后再抛出超限异常。现在先用严格 UTF-8 编码器计算长度，合格才分配并填充等长数组。恰好 1 MiB 仍能发送；孤立代理字符仍触发编码异常。五个 AMQP 头一次预留字典容量。[编码器](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.RabbitMQ/RabbitMqWireCodec.cs)

例如 `profile.changed.v1`，校验时只需把契约名的 UTF-8 字节数、`.v` 的两个字节和正整数版本号的十进制位数相加。总数必须小于 255。校验通过后才拼接别名，并让消息类型和实际发布使用同一个字符串。[路由键校验](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.Abstractions/MessageRoutingKey.cs) · [发送入口](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.RabbitMQ/RabbitMqTransport.cs)

| 内存内操作 | 旧耗时 | 新耗时 | 旧托管 B/次 | 新托管 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 校验短 ASCII 路由键 | 35.9 ns | 17.3 ns | 64 | 0 |
| 校验 254 字节 Unicode 路由键 | 79.7 ns | 35.7 ns | 264 | 0 |
| 编码小 ASCII 消息及其属性 | 481.7 ns | 361.5 ns | 952 | 752 |
| 编码恰好 1 MiB 的多字节正文及其属性 | 774.9 µs | 767.4 µs | 1049512 | 1049312 |
| 拒绝超限 1 字节的多字节正文 | 656.5 µs | 71.9 µs | 1048968 | 296 |

合法大消息仍需一个用于发送的完整数组。超限消息仍需扫描字符并抛出异常，所以拒绝路径也有耗时与少量分配。

## 复现与回归

2026-11-25，Windows x64、Intel Core i5-9400、SDK 11.0.100，Release 构建。将 Git `e7de6e2` 导出到独立目录，复制本次基准项目，并给予基线 RabbitMQ 项目相同的基准程序集内部访问权限。两个版本用相同输入，按旧新、新旧、旧新交错运行。每个进程各取五轮的中位数，再按版本取三个进程的中位数。[编码基准](../../../MS.Microservice.Messaging/benchmarks/MS.Microservice.Messaging.Benchmarks/WireBenchmarks.cs) · [死信查询基准](../../../MS.Microservice.Messaging/benchmarks/MS.Microservice.Messaging.Benchmarks/DeadLetterBenchmarks.cs)

```powershell
$bench = 'MS.Microservice.Messaging/benchmarks/MS.Microservice.Messaging.Benchmarks'
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project $bench -- --wire
dotnet run -c Release --no-build --project $bench -- --dead-letters
dotnet test MS.Microservice.Messaging/MS.Microservice.Messaging.slnx -c Release
```

死信基准通过公开接口查询 SQLite 内存数据库，建表、填充和字段核对均在计时外，使用进程托管分配计数。编码基准校验生成的字节与属性，通过当前线程计量分配，计时范围截至编码器返回或拒绝消息。表中耗时未包含 PostgreSQL 网络往返、真实 RabbitMQ 写入或等待 broker 确认。

Release 测试通过 **161** 项（Abstractions 44、RabbitMQ 35、SelfManaged 55、Wolverine 22、集成测试项目 5），失败 0 项。另有 12 项真实 PostgreSQL/RabbitMQ 容器测试跳过，本机默认 Docker 引擎管道不可用。新增回归覆盖精确字节上限、含非法 UTF-16 的超限输入、死信时间回退、同刻排序和消费方名称转义。
