# 存活与就绪检查

## 原来的问题

Reference 原先直接在 `/health/ready` 端点里依次检查数据库、迁移、消息存储和 RabbitMQ。检查逻辑与 HTTP 响应写法绑在一起，其他宿主要复制端点才能获得相同的存活、就绪边界。停机开始后，该端点仍可能报告就绪，让代理继续送入新请求。

健康检查有两种不同用途。存活检查回答进程是否仍能处理请求，不应因为数据库或 Broker 暂时不可用而触发进程重启。就绪检查回答当前实例是否应接收业务流量，需要反映数据库、消息存储和停止接流量的状态。把所有依赖都接到存活端点会把依赖故障误判为进程故障；忽略停机信号则会延长实例撤出流量的时间。

## 本次方案

`AddPlatformHealthChecks` 返回 ASP.NET Core 的 `IHealthChecksBuilder`。宿主显式注册探针，并给参与就绪判定的探针加 `ready` 标签。`MapPlatformHealthChecks` 映射两个匿名 GET 端点：

| 路径 | 执行内容 | HTTP 状态 |
| --- | --- | --- |
| `/health/live` | 不访问依赖，返回 `{"status":"healthy"}`。 | `200` |
| `/health/ready` | 先检查 `ApplicationStopping`；若已停止接流量则立即返回 `{"status":"unhealthy","reason":"stopping"}`，否则只执行带 `ready` 标签的探针。 | 健康或降级为 `200`；不健康或停止中为 `503`。 |

通用映射默认只写 `status` 字段；宿主可以提供静态响应写入方法。Reference 用它保留原来的 JSON 字段：健康和降级返回 `status`、`messaging`；不健康返回 `status`、`reason`。数据库有待执行迁移时 `reason` 为 `pending_migrations`，其他探针失败时为 `storage_unavailable`。停机状态新增 `stopping` 原因。Reference 注册两个就绪探针：持久存储探针依次检查数据库、迁移和消息存储；Broker 探针检查连接。每个探针有 5 秒期限。Broker 返回不可用时记为 `Degraded`，维持 `200`，因为业务写入仍可持久化；探针抛出异常则按不健康处理。

内置 `HealthCheckService` 可以并发运行不同探针。数据库与消息存储共用持久层，因此放在同一个探针内保持原来的检查顺序：发现待迁移时就不再访问消息存储，并优先报告 `pending_migrations`。Broker 探针可以与持久存储探针并行。就绪端点在停机信号已到达时跳过探针；对于信号到达前已经开始的检查，仍依赖检查期限和取消 token 结束，不会强行终止底层 I/O。

Reference 的限流和请求超时策略只附在业务 API 路由组，两个健康端点不占用 API 配额，也不使用 API 请求期限。
通用映射还显式豁免全局限流器和全局默认请求超时；宿主即使选择全局策略，探针也不会消耗业务额度或因业务期限返回 `429`、`504`。依赖探针自身的超时仍由各自的 `HealthCheckRegistration` 控制。

## AOT 与验证

通用端点使用明确的 `RequestDelegate`、泛型服务解析和 `Utf8JsonWriter`；Reference 使用显式工厂注册探针。新增代码不扫描类型、不绑定配置对象，也不通过匿名对象运行时序列化健康响应。这里是功能与生命周期修正，没有性能基准数据。

TestServer 覆盖：存活检查不触发依赖、就绪只执行 `ready` 探针、健康/降级/不健康状态映射、停机立即拒绝且不运行依赖、调用方取消传递给探针；Reference 测试验证待迁移、消息存储故障、Broker 降级以及 JSON 字段。真实数据库、Broker 与部署平台的摘流行为仍需在集成环境验证。
