# Profile 与 Order 如何共用 HTTP 幂等

客户端可能在创建成功后没有收到 `201`。直接重试会再次运行业务：Profile 通常被唯一约束挡住，却无法取回第一次的响应；Order 没有用请求内容做唯一约束，同样的创建请求会产生第二笔订单。幂等记录保存第一次成功响应，让同一请求键的重试取得原来的状态码、`Location` 和正文。

## 一个宿主接入，两处路由标记

`ReferenceHost` 在 `Http:Idempotency:Enabled=true` 时注册存储、身份作用域、共用执行器和清理任务。路由仍要单独选择是否使用：

| 路由 | 标记 | 普通业务入口 |
| --- | --- | --- |
| `POST /api/v1/profiles` | `.RequireHttpIdempotency("profiles.create")` | `ProfileService.CreateAsync` |
| `POST /api/v1/orders` | `.RequireHttpIdempotency("orders.create")` | `OrderService.CreateAsync` |

标记只包裹该路由的请求委托，并在模型绑定前运行。全局开关关闭，或者请求没有 `Idempotency-Key` 时，两个接口直接运行原业务逻辑；其他未标记接口即使带键，也不查幂等表。Profile 的授权策略和 Order 的认证要求仍由各自路由处理。共用的 `ReferenceIdempotencyActorScope` 从已经验证的身份中读取 issuer 和 subject，以隔离不同用户的幂等记录；它不判断业务权限。Order 创建时另将 issuer 和 subject 存为订单所有者，按 ID 读取时也按所有者筛选：其他身份请求同一订单 ID 得到 `404`。幂等记录隔离不能代替订单读取的权限边界。

Profile 与 Order 都调用同一个 `ReferenceHttpIdempotencyExecutor`。执行器只接收操作名和端点委托，负责读取键、计算指纹、占键、执行端点、保存并重放响应；它不读取 `CreateProfile` 或 `CreateOrder`，也不调用业务服务。因此新增路由仍要实现自己的业务和选择操作名，但不必再写一套幂等 Handler。MVC Action 可用同一个 Resource Filter；正式宿主的这两个接口目前都是 Minimal API。

## 同一个键怎样比较

记录先按**操作名、已认证身份、请求键**定位；`profiles.create` 与 `orders.create` 可以各自使用同一个键。找到记录后，执行器比较 Method、Path、QueryString、完整的 `Content-Type` 和 Body 表示。JSON 对象的字段按名称递归排序，字符串之外的空白忽略，数组顺序保留；非 JSON Body 按原始字节比较。重复 JSON 字段名，包括仅大小写不同的名称，在绑定前返回 `400`。

例如，Order 首次以键 `K1` 提交 `{"sku":" SKU-42 ","quantity":2}`，返回 `201`。同一身份、同一路径、同一 `Content-Type` 以 `K1` 提交 `{"quantity":2,"sku":" SKU-42 "}`，得到第一次的 `201`、`Location` 和正文字节，不再创建订单。把 `quantity` 改成 `3` 则返回 `409`；改用 `K2` 提交原 Body 是一次新的创建。OrderService 会修剪 SKU，但指纹比较发生在业务处理之前，所以 `" SKU-42 "` 与 `"SKU-42"` 使用同一键仍视为不同请求。Profile 的对象字段重排同样会重放；改变 `roles` 数组顺序会返回 `409`。

旧 `profiles.create` 记录的请求哈希来自绑定后重新序列化的模型，与当前 HTTP 指纹不同。相同身份和键命中旧记录时返回 `409`，不做旧格式查询或转换，也不执行创建。换新键只能发起新请求，不能找回旧响应；Profile 的业务唯一约束仍可能拒绝重复创建。

## 为什么 Order 的内层工作单元不会单独提交

首次带键请求查不到记录时，执行器调用 `IUnitOfWork.ExecuteAsync` 开启最外层事务，`EfCoreIdempotencyStore` 先在同一 `ReferenceDbContext` 中插入唯一键占位并刷新，再运行原端点。`OrderService.CreateAsync` 随后也调用 `IUnitOfWork.ExecuteAsync`。此时请求作用域内的工作单元已经处于活动状态，内层调用只执行回调；`OrderRepository.Add` 将订单加入同一个 `DbContext`，不会另开事务或提前提交。执行器捕获端点实际返回的 `201`，保存响应快照，外层工作单元再提交订单和幂等记录。Profile 的档案、Outbox 和响应快照也参加它的外层事务。

业务返回 `4xx` 或执行失败时，占位随外层事务回滚，修正请求后可以复用键。同键并发写入发生唯一约束冲突后，输家退出事务，用新的 DI 作用域查询胜者：指纹相同就重放，不同就返回 `409`。这依赖业务写入、幂等记录和 Outbox 使用同一个事务；事务外的邮件或网络调用不受保护。

## 当前取舍与验证范围

Reference 只保存 `2xx` 响应，带键请求 Body 上限为 1 MiB，响应正文上限为 64 KiB；快照只包含状态码、可为空的 `Content-Type`、`Location` 和正文。流式输出、文件下载或依赖其他响应头的接口不能直接套用。宿主不自动建表；部署前要应用所选提供者的迁移。两种迁移流都先有 `AddHttpIdempotency`，后有新增的 `AddOrders`，所以标准完整迁移即使在功能关闭时也会建幂等表；关闭仅表示请求运行时不访问该表。记录保留 24 小时，清理任务每小时运行一次。

测试代码覆盖两个创建路由的重放和冲突、JSON 字段重排、旧指纹同键冲突、禁用开关及事务回滚。本次没有延迟、分配或 GC 的前后基准数据，也没有完成整个 Reference 宿主的 Native AOT 发布验证。真实 PostgreSQL 并发、Wolverine 原生 Outbox 和进程崩溃恢复仍需对应环境验证。
