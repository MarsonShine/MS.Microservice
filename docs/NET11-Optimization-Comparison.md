# .NET 11 升级前后：循环与新特性对照

本记录面向维护者，比较项目中已有实现与 .NET 11／C# 15 的对应能力。这里的“优化”分别指代码可读性、接口能力或运行时性能；没有基准数据的改写不视为性能提升。完整升级背景见 [.NET 11 升级过程](NET11-Upgrade-Union-Async-Validation.md)。

## 嵌套循环和标签跳转

C# 15 允许为外围循环命名，并从内部用 `break label;` 或 `continue label;` 控制该循环。它主要替代跨层传播的布尔标志或 `goto`，不是加速嵌套循环的算法。语法与跳转范围见 [微软语言文档](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/jump-statements)。

本次对 Git 跟踪的 1,137 个 C# 文件按 Roslyn 语法树扫描，发现 145 个处于另一循环内部的循环节点；按路径排除测试和基准后约 65 个。全仓未发现 `goto`。进一步用语法启发式检查内层赋值、外层读取的标志，没有找到可直接消除的跨层退出标志。这不是数据流证明：跨方法或隐式状态仍可能需要人工调查。

| 现有路径 | 升级前控制方式 | .NET 11 后的判断 |
| --- | --- | --- |
| [ICollectionExtensions](../src/MS.Microservice.Core/Extension/ICollectionExtensions.cs) | 内层 `break` 只停止当前内层循环 | 保持原样；跳出外层会改变结果。 |
| [ExternalIdentity](../src/MS.Microservice.AspNetCore/ExternalIdentity.cs) | 找到匹配后直接 `return` | 已退出方法，额外标签没有减少状态。 |
| [JobExecutor](../MS.Microservice.Tasks/src/MS.Microservice.Tasks/JobExecutor.cs) | 针对当前队列结束扫描，并经 `finally` 释放资源 | 保持原有作用域；跨层跳转可能改变调度与清理。 |
| [CsvHelper](../src/MS.Microservice.Excel/CsvHelper.cs) | `quote` 记录字段输出格式 | 不是跨层退出标志，不能替换为标签。 |

因此本轮没有为了使用语法而改写嵌套循环。若将来出现真实跨层退出，可在目标循环加标签，并验证释放、迭代次数与退出后的处理保持一致。

## 标准库与语言特性

| 特性 | 升级前与项目证据 | 升级后判断 |
| --- | --- | --- |
| UTF-16 有效性检查 | [MessageMetadataLimits](../MS.Microservice.Messaging/src/MS.Microservice.Messaging.Abstractions/MessageMetadataLimits.cs) 原用严格 UTF-8 字节计数检查三个元数据字段。 | TraceParent／TraceState 只需要验证可编码，改用 `Utf16.IsValid`；CorrelationId 仍需同时限制 UTF-8 字节数，保留严格编码计数。无需靠异常判断前两个字段的无效代理项；未测吞吐，不能声称更快。 |
| `Process.RunAndCaptureTextAsync` | [ProcessRunner](../src/MS.Microservice.Processes/ProcessRunner.cs) 限制双流输出，区分超时／取消，终止进程树并报告清理错误。 | 新的一次性捕获示例没有证明覆盖这些契约，暂不替换。 |
| Base64 与 Unicode 字符 API | 加密、搜索游标和渲染路径使用 `Convert.FromBase64String`；部分调用依赖格式异常或后续检查。 | 新的 span／Rune API 需要逐调用点验证格式、异常及缓冲生命周期；本轮未找到可证明的等价收益，不改变安全边界。 |
| `StringBuilder.MoveChunks` | 文本提取、模板与进程输出使用 StringBuilder，未发现明确的构建器所有权转移瓶颈。 | 转移会清空源实例；未找到需要这种语义的现有路径。 |
| JSONL、DNS、FullJoin | 全仓搜索 `SerializeAsyncEnumerable`、`Dns` 与 `FullJoin` 未发现现有直接调用。 | JSONL 会改变输出格式，自定义 DNS 和完整外连接需要具体需求，暂不引入。 |
| 内存流新类型 | 现有若干路径已使用 `MemoryStream(byte[], writable: false)`，不会因为构造而复制输入。 | 未找到明确的只读内存转数组后再建流的等价候选；不据 API 名称推断收益。 |
| union / JSON | Reference 事务和幂等查询已使用 union；共享 `Either` 有现存公开契约。 | 保持当前边界；自动 union JSON 表示可能改变线格式，不能机械替换。详见 [升级过程](NET11-Upgrade-Union-Async-Validation.md)。 |
| 异步数据注解 | Lab 创建用户的角色目录查询已有真实异步验证；Excel/CSV 提供显式入口。 | 保持事务、取消和错误映射；MVC 不会自动运行 Minimal API 的异步验证机制。 |
| `closed` 层次 | Lab 的 [订单命令和事件](../samples/Lab/MS.Microservice.Lab.Domain/Aggregates/OrderAggregate/OrderMessages.cs)是公开抽象类型，现有未知分支有处理逻辑。 | 封闭继承会改变扩展和未知事件契约，未采用。 |
| 集合表达式参数、扩展索引器 | 可简化特定构造或索引器声明。 | 审查未发现需要据此改变公共 API 才能获得明确收益的候选；不为展示语法增加抽象。 |
| 旧运行时静态接口成员 | 解决面向较旧目标框架的特定接口兼容问题。 | 项目统一面向 net11.0，未发现迁移需求。 |
| 内存安全 | C# 15 的该能力仍需预览特性和额外标志。 | 不改变全局编译规则；需要独立范围、兼容与性能证据。 |

标准库功能清单见 [.NET 11 官方说明](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/libraries)，语言特性见 [C# 15 官方说明](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15)。

## 运行时、Web 和数据访问

- **JIT 与硬件**：边界检查消除、去虚化和向量化等由运行时在适用时执行；这些并不保证本项目所有循环变快。本轮未做跨版本同条件性能测试，不能量化收益；部署方仍需核对 [.NET 11 最低硬件要求](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime)。
- **Runtime Async**：项目级功能需显式启用 `runtime-async=on`；官方说明 net11.0 不再需要 `EnablePreviewFeatures`。本项目存在 `AsyncLocal` 日志上下文，整体启用需要独立验证上下文传播、诊断与性能；本轮不更改项目配置。
- **ASP.NET Core**：已有 Minimal API 验证和 union 使用；原生 union JSON 或 OpenAPI 更新会影响协议或工具兼容，不以换 API 为目的改变响应。参见 [ASP.NET Core 11](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-11)。
- **EF Core**：当前 EF Core 和 PostgreSQL 提供程序为 10.x；EF 11 的查询特性不能仅凭目标框架升级而使用。提供程序升级需另行核验版本约束及数据库集成，参见 [EF Core 11](https://learn.microsoft.com/ef/core/what-is-new/ef-core-11.0/whatsnew)。

验证命令、执行结果和未覆盖环境见 [本次验收](validation/DOTNET11-OPTIMIZATION-AUDIT/summary.md)。
