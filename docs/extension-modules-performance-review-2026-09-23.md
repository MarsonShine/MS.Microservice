# AI、Logging、Messaging、Persistence 性能检查（2026-09-23）

本轮先检查四个模块的实际调用路径，再对候选点测量。只有局部方法有可重复收益且原有语义可由测试覆盖时才保留代码修改。所有微基准在 Windows x64、.NET 10.0.12、Release 下运行；它们没有测量网络、数据库或日志后端的完整吞吐量。

| 模块 | 处理结果 | 测量依据 |
| --- | --- | --- |
| AI | `027e2e6`：DeepSeek 配置校验改用静态 Provider 选择器 | 每类能力 128 个模型时，校验由 19,084.9 ns / 480.4 B 降至 6,722.0 ns / 368 B。该校验主要发生在配置验证时；详见 [AI 说明](../MS.Microservice.AI/docs/performance-2026-09-23.md)。 |
| Logging | `14a52f4` 的直接存值优化已由后续生命周期修正取代 | 直接存值时普通 Push/Dispose 为 100.8 ns / 104 B，但释放后的子任务仍能读到旧请求上下文；可清空 Holder 为 117.3 ns / 136 B，并使子任务的 ambient 查找失效。详见 [Holder 说明](../MS.Microservice.Logging/docs/async-local-holder-lifetime.md)。 |
| Messaging | `65ac46c`：按 consumer 名称索引订阅 | 单订阅查找由 67.8 ns / 120 B 降至 16.6 ns / 0 B。接收器每条消息都会调用该查找；详见 [Messaging 说明](../MS.Microservice.Messaging/docs/design.md#consumer-查找成本)。 |
| Persistence | 不改动 | EFCore 审计时间戳的 LINQ 过滤候选在单实体场景变快，但 32 实体场景变慢；未保留。SqlSugar 序列化与 ORM 查询没有定位到能在本层安全替换的热点。 |

Persistence 的审计扩展实验关闭了 EF Core 自动变更检测，以单独观察扩展方法；固定跟踪实体为 `Modified`，每场景 5 轮、每轮 20,000 次：

| 跟踪实体数 | 原实现 | 临时移除 LINQ 过滤 | 结论 |
| ---: | ---: | ---: | --- |
| 1 | 464.8 ns / 184 B | 161.0 ns / 128 B | 候选更快 |
| 32 | 670.5 ns / 1176 B | 845.3 ns / 1120 B | 候选变慢；独立重复运行约 841.1 ns |

没有实际保存操作的实体数分布数据，无法证明该候选能改善整体工作负载，故已撤销生产改动及临时基准。EFCore 9 项、SqlSugar 20 项测试通过。

AI 解决方案测试 144 项通过；Logging 四个测试项目当前 28 项通过；Messaging 本地测试 117 项通过，另有 12 项真实依赖集成测试按配置跳过；架构测试 39 项通过。DeepSeek 项目使用 `IsAotCompatible=true` 的分析构建为 0 警告，但这不等于整个 AI 模块已通过 Native AOT 发布验证。

## 后续方向（本轮未实现）

- AI 模型路由的线性查找已测量，但返回配置中原始大小写的 Scenario 名称和运行期可变配置是现有契约。加入缓存前需要明确失效机制，并用请求级数据证明收益。
- AI QuestionGeneration 的运行期 JSON 类型处理、Messaging 的运行期 `Type` 序列化与 Wolverine 泛型注册、Persistence 的软删除查询过滤器动态泛型方法及 SqlSugar 的 `object` 序列化，都需要分别设计或验证 Native AOT 路径。此次优化没有引入新的反射。
- Logging 的 NLog XML 配置和 Serilog 配置读取，以及这些模块依赖的第三方框架，仍需在目标发布配置下做 Native AOT 验证。上述局部基准不能替代该验证。
