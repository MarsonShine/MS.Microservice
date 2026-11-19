# .NET 11 特性与嵌套循环审查验收

日期：2026-11-17；基线 9466209，验收对象为该基线加本任务工作区改动。SDK 11.0.100，Windows。

全仓 1,137 个 Git 跟踪的 C# 文件通过 Roslyn 语法树识别 145 个嵌套循环节点；跨层标志检查是启发式，未找到明确适合标签跳转的生产候选，不代表静态分析证明所有跨方法情况。主要新特性、依据及不采用原因见 [升级前后对照](../../NET11-Optimization-Comparison.md)。

消息 TraceParent/TraceState 改为 `Utf16.IsValid`；CorrelationId 保留严格 UTF-8 字节数上限。测试覆盖代理对、孤立代理项、边界长度及消息序列化／反序列化。

- Messaging Abstractions.Tests：44 通过、0 失败。
- Messaging RabbitMQ.Tests：34 通过、0 失败。
- `dotnet build -c Release --verbosity quiet`：0 警告、0 错误。
- `git diff --check` 和 harness 检查：通过。

未执行性能基准、完整解决方案测试、真实 RabbitMQ 或其他外部服务验证；不以语法变化或 API 替换宣称速度提升。
