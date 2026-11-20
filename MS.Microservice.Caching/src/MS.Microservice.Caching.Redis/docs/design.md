# Redis 适配的组合与生命周期

本适配把宿主选择的 Redis 接入已有缓存治理。业务键、TTL、源加载预算及 Meter 均保留在 Caching；序列化和两级读取由 HybridCache 负责；Redis 命令、连接及重连由官方 provider 负责。使用和配置见 [README](../README.md)。

## 注册边界

启用时只允许一个治理入口和一个未命名的 `IDistributedCache`。重复注册直接失败，避免依赖 DI 的最后一次注册覆盖而悄悄更换后端。Redis 配置采用公开的 `RedisCacheOptions`，不再维护第二套连接字符串解析或 Redis CRUD。

注册时检查端点和前缀，拒绝外部 `ConnectionMultiplexerFactory`；这些检查只验证配置契约，无法判断网络、认证、TLS 或 Redis 是否可用。连通性属于运行时操作与部署检查。宿主后续也不应再覆盖相同服务或修改连接所有权相关选项。

`Enabled=false` 只注册关闭状态的治理层，不执行 Redis 回调，因此本地开发或禁用缓存时无需存在 Redis 配置。读取直接走源，绕过缓存预算和缓存指标；禁用不是故障时动态切换模式。

## 读取与序列化

~~~text
IGovernedCache：键、策略、读取计数
  → HybridCache L1：进程内 MemoryCache
  → HybridCache L2：官方 RedisCache / IDistributedCache
  → Redis
  → 未命中：同实例同键合并 → 领取回源预算 → 业务工厂
  → HybridCache 发布结果、填充本地缓存，并尝试回填 L2
~~~

L1 是本地内存，L2 才是共享 Redis。两层并不是两个治理实例，也不需要业务分别访问。官方 provider 支持缓冲式分布式缓存接口，HybridCache 能利用此接口处理序列化 payload；适配不包一层 byte[] JSON 扩展，以免丢失原生能力或重复序列化。

共享 payload 包含 HybridCache 的格式信息，再由 Redis provider 管理缓存数据和过期元数据。它不是业务约定的裸 JSON。键中应用／region／摘要和 provider 的 `InstanceName` 前缀共同隔离命名空间；摘要隐藏原文，不提供访问控制或加密。

## 连接所有权

官方 RedisCache 懒加载创建连接，并在后续操作中复用，不按请求连接 Redis。容器持有并释放 provider；provider 释放它持有的 multiplexer。连接重建也沿用官方实现与 StackExchange.Redis 的行为。

`ConnectionMultiplexerFactory` 虽是公开 API，但不能据此推断返回连接的所有权仍属于调用方。官方实现释放 provider 时会关闭并释放 `IDatabase.Multiplexer`，启用 ForceReconnect 的路径也可能释放旧连接。因此本适配拒绝该工厂，保持“创建者与释放者都是 provider”的单一责任，避免关闭业务、健康检查或订阅共用的连接。[RedisCache v10.0.6 源码](https://github.com/dotnet/aspnetcore/blob/v10.0.6/src/Caching/StackExchangeRedis/src/RedisCache.cs)

本包不向 DI 暴露 provider 的内部连接。现有 `HealthChecks.Redis` 使用宿主另行注册的 `IConnectionMultiplexer`；组合时那是另一个独立连接，不应将健康检查的成功解释为缓存 provider 已完成相同连接上的一次操作。若以后需要共享连接，应先明确所有权、断线恢复与释放契约，再选择适配方式。

## 集群发现与连接预算

当前 StackExchange.Redis 3.3.1 在握手时读取 `CLUSTER SLOTS` 和 `CLUSTER NODES`；首轮识别到已连接的 Cluster 端点后，再主动连接发现的端点。发现条件没有排除副本 seed，不能把“从副本开始连接”直接判为不可写的原因。拓扑发现使用 SLOTS／NODES，不依赖 `CLUSTER SHARDS`。[当前版本连接源码](https://github.com/StackExchange/StackExchange.Redis/blob/0a92ae43b0f8e80467920115a54f9761cc04ee4c/src/StackExchange.Redis/ConnectionMultiplexer.cs)

seed 只提供进入集群的入口，实际键根据槽位路由到相应主节点。所有公布的服务端点必须对应用可达，地址、端口、DNS 和认证也要与该部署匹配；不能用一次 seed PING 或手工取得节点列表证明完整路由已经建立。

首次连接还包括每个端点的握手、拓扑应用与原生连接重试。整体测试取消、单次连接超时、命令超时分别约束不同阶段；将 `ConnectRetry` 人为置零或大幅压缩连接超时会改变发现行为，使测试失败无法代表宿主的正常配置。验证应保留目标连接策略，并为初始发现留出独立预算，性能采样在连接和缓存项预热之后开始。适配不通过特殊 seed、固定主节点列表或版本分支绕过客户端发现。

## 异常与降级

以下说明依据当前 HybridCache 10.5.0 对应的官方实现；升级依赖时需重新验证相关行为。[源实现](https://github.com/dotnet/extensions/blob/958df529b628f62daa615dba271f42e2225d7a97/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.StampedeStateT.cs)

- L1 命中不访问 Redis。L2 普通读取异常记录日志后按 miss 继续执行工厂；共享取消令牌已取消时，取消异常继续传播。
- 工厂异常不会作为正常缓存值保存。预算耗尽在工厂开始前拒绝，源计数和时长遵循治理层原有口径。
- 加载后的结果和本地缓存可先于 L2 回填完成发布。L2 回填错误记录日志，不把已经取得的业务结果变成缓存写入失败；因此返回成功不能作为跨实例立即读到该值的确认。
- 失效及主动写入是各自的调用路径，不能从读取降级推导其成功保证。宿主需要处理调用失败，并为重要失效设计可靠通知；本适配不增加通用 catch 后直接成功的包装。

源预算约束单实例工厂并发，不约束 Redis 等待、命令队列或所有实例总负载。Redis 故障期间，连接／命令超时仍可能消耗请求时间。禁用、绕过和普通缓存读取也是不同路径：前两者直接调用源，不受缓存源预算保护。适配没有新增重试、熔断或后台预热，避免与原生连接恢复重复工作。

## TTL 与跨实例一致性

本地 TTL 不超过共享 TTL，HybridCache 从 L2 装载时还会约束本地有效期，不使它超过共享条目剩余的整体寿命。Redis 内存淘汰或重启可能使 L2 提前未命中，TTL 不是可用性承诺。

删除清理当前实例和 L2，不广播给其他 L1；正在进行的旧读取也可能随后回填。因此仅缩短本地 TTL 可以减小陈旧窗口，不能实现严格一致。数据库提交和失效没有共同事务，已有 Outbox 可以保证通知最终产生，但队列竞争消费不等于全实例广播。实时授权继续查询权威数据库。

## 监测与性能验证

适配只增加后端注册，热路径继续调用同一个 `IGovernedCache`。治理 Meter 保留区域级统计，Observability 负责订阅和导出；精确层级命中、连接健康和 Redis 服务端容量需采用相应后端诊断。

本地治理基准不能扩大为 Redis 验收。实际验证应分开记录预热后的 L1 命中、绕过 L1 的 L2 读取、冷键回源，以及源工厂实际执行次数；说明序列化类型、大小、并发、运行时、服务端和网络条件。正确性验证至少需要独立容器共享、过期、失效和释放隔离。未验证的认证／TLS、主从切换或容量上限应明确保留为部署责任，不因模拟后端或单机结果而声称覆盖。
