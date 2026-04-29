# Order 事件溯源：从 Controller 到核心层的完整示例

本文把仓库中的 PostgreSQL + Event Sourcing 示例串成一条完整调用链，重点说明：

- Controller 如何接收 HTTP 请求
- Application Service 如何编排命令
- Domain/Core 如何用 `Decide` / `Evolve` 表达业务规则
- Infrastructure 如何把事件持久化到 PostgreSQL，并更新读模型
- 调用方应该如何使用这些接口

## 1. 涉及的关键文件

### Web / API
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Web/Controller/OrdersController.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Web/Application/Orders/OrderWorkflowAppService.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Web/Application/Orders/OrderQueryAppService.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Web/Application/Models/Orders/*`

### Domain / Core
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Domain/Aggregates/OrderAggregate/OrderMessages.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Domain/Aggregates/OrderAggregate/OrderState.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Domain/Aggregates/OrderAggregate/OrderAggregate.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Domain/EventSourcing/EventSourcingAbstractions.cs`

### Infrastructure / PostgreSQL
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Infrastructure/EventSourcing/EventStoreDbContext.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Infrastructure/EventSourcing/Repository/PostgresEventStore.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Infrastructure/EventSourcing/Repository/PostgresSnapshotStore.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Infrastructure/EventSourcing/Repository/PostgresProjectionCheckpointStore.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/src/MS.Microservice.Infrastructure/EventSourcing/Orders/OrderReadModelProjector.cs`
- `/home/runner/work/MS.Microservice/MS.Microservice/docs/postgresql/event-sourcing-order.sql`

---

## 2. Controller 层：只处理 HTTP，不写业务规则

`OrdersController` 提供了 6 个接口：

- `POST /api/v1/orders/{orderId}/create`
- `POST /api/v1/orders/{orderId}/items/add`
- `POST /api/v1/orders/{orderId}/items/remove`
- `POST /api/v1/orders/{orderId}/confirm`
- `POST /api/v1/orders/{orderId}/cancel`
- `GET /api/v1/orders/{orderId}`

Controller 的职责只有三件事：

1. 接收 HTTP 参数
2. 调用应用服务
3. 把 `Either<Error, TResult>` 转成 HTTP 响应

它**不直接访问数据库**，也**不直接写 Decide/Evolve 规则**。

### 以“创建订单”为例

控制器入口：

```csharp
[HttpPost("{orderId:guid}/create")]
public async Task<IActionResult> Create(Guid orderId, [FromBody] CreateOrderRequest request)
    => Ok(await ExecuteAsync(() => _workflowAppService.CreateAsync(orderId, request, HttpContext.RequestAborted)));
```

可以看到它只是把：

- 路由中的 `orderId`
- body 中的 `customerId` / `currency`
- `RequestAborted`

传给 `OrderWorkflowAppService`。

---

## 3. Application 层：把 HTTP 请求编排成领域命令

### 3.1 命令映射

`OrderWorkflowAppService.CreateAsync` 会把 Web 请求映射成领域命令：

```csharp
new CreateOrder(orderId, request.CustomerId, request.Currency)
```

这一层负责“编排”，不负责业务真相。它做的事包括：

- 把 Request DTO 转成领域命令
- 组装事件元数据（`CorrelationId`、`UserId`、`TraceId`）
- 调用 `OrderCommandService`
- 在成功后触发 `OrderReadModelProjector`
- 再读取一次事件流，返回当前版本、总金额、状态等结果

### 3.2 为什么要在这里触发 projector

在这个示例里，我把投影器放在应用层同步触发，是为了让 Controller 示例更完整：

- 命令写入成功
- 读模型立即更新
- 随后 `GET /orders/{id}` 就能直接看到投影结果

这是一种**演示型实现**。未来如果你要改成标准 CQRS，可以把 projector 放到后台订阅器里异步执行。

---

## 4. Domain/Core 层：业务规则的真正落点

关键文件是：

- `OrderMessages.cs`
- `OrderState.cs`
- `OrderAggregate.cs`

### 4.1 Command

例如：

- `CreateOrder`
- `AddOrderItem`
- `RemoveOrderItem`
- `ConfirmOrder`
- `CancelOrder`

### 4.2 Event

例如：

- `OrderCreated`
- `OrderItemAdded`
- `OrderItemRemoved`
- `OrderConfirmed`
- `OrderCancelled`

### 4.3 State

`OrderState` 不是 ORM 实体，而是一个纯状态对象，包含：

- 是否已创建
- 是否已确认
- 是否已取消
- 客户、币种
- 行项目集合
- 总金额
- 当前版本号

### 4.4 Decide：决定产生什么事件

比如确认订单时：

- 如果订单不存在，返回错误
- 如果订单已取消，返回错误
- 如果没有商品行，返回错误
- 否则返回 `OrderConfirmed`

也就是说：

> 决策结果不是“直接改数据库”，而是“决定接下来要追加哪些事件”。

### 4.5 Evolve：根据事件推进状态

例如：

- `OrderCreated` 让 `Exists = true`
- `OrderItemAdded` 增加商品行和总金额
- `OrderConfirmed` 让 `IsConfirmed = true`

这就是函数式事件溯源的核心：

```csharp
state = history.Aggregate(initialState, Evolve)
newEvents = Decide(state, command)
```

---

## 5. Infrastructure 层：把事件写入 PostgreSQL

### 5.1 EventStoreDbContext

`EventStoreDbContext` 定义了 4 张核心表：

- `event_store`
- `snapshots`
- `projection_checkpoint`
- `order_read_model`

对应 SQL 在：

`/home/runner/work/MS.Microservice/MS.Microservice/docs/postgresql/event-sourcing-order.sql`

### 5.2 PostgresEventStore

`PostgresEventStore.AppendToStreamAsync` 的流程是：

1. 查当前 stream 的最大版本号
2. 检查是否等于 `expectedVersion`
3. 把新事件序列化后追加到 `event_store`
4. 如果 `(stream_id, version)` 唯一约束冲突，则抛并发异常

这里体现的就是 optimistic concurrency。

### 5.3 SnapshotStore

`PostgresSnapshotStore` 负责：

- 读取某个聚合的最新快照
- 按 stream upsert 快照

示例中 `OrderCommandService` 默认每 100 个事件打一次快照。

### 5.4 Projection

`OrderReadModelProjector` 从 `event_store` 的全局位置往后拉取事件，并更新：

- `order_read_model`
- `projection_checkpoint`

所以读侧查询不需要每次都扫全事件流。

---

## 6. 一次完整请求到底经历了什么

以 `POST /api/v1/orders/{orderId}/items/add` 为例：

### Step 1：Controller
`OrdersController.AddItem` 收到请求。

### Step 2：Application Service
`OrderWorkflowAppService.AddItemAsync`：

- 把请求转成 `AddOrderItem`
- 组装 `EventMetadata`
- 调用 `OrderCommandService.HandleAsync`

### Step 3：Command Service
`OrderCommandService.HandleAsync`：

- 从 `ISnapshotStore` 读取快照
- 从 `IEventStore` 读取增量历史事件
- `Fold` 出当前 `OrderState`
- 调 `OrderAggregate.Decide`
- 把新事件追加到 PostgreSQL
- 满足条件时写快照

### Step 4：Projector
应用层调用 `OrderReadModelProjector.ProjectAsync`：

- 从 checkpoint 之后读取全局事件
- 更新 `order_read_model`
- 更新 `projection_checkpoint`

### Step 5：返回给客户端
应用层再读取一次当前状态，返回：

- 当前版本
- 本次追加的事件类型
- 当前总金额
- 当前状态

---

## 7. 如何调用这些接口

假设订单号是：

`3fa85f64-5717-4562-b3fc-2c963f66afa6`

### 7.1 创建订单

```http
POST /api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/create
Content-Type: application/json

{
  "customerId": "cust-001",
  "currency": "CNY"
}
```

### 7.2 添加商品

```http
POST /api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/items/add
Content-Type: application/json

{
  "productId": "sku-apple",
  "unitPrice": 12.5,
  "quantity": 2
}
```

### 7.3 再添加一个商品

```http
POST /api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/items/add
Content-Type: application/json

{
  "productId": "sku-book",
  "unitPrice": 30,
  "quantity": 1
}
```

### 7.4 确认订单

```http
POST /api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/confirm
```

### 7.5 查询订单

```http
GET /api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6
```

返回结果里会包含：

- 当前状态（Draft / Confirmed / Cancelled）
- 总金额
- 行项目
- 事件列表
- 读模型最后更新时间

---

## 8. 这个例子最重要的设计点

### 8.1 Controller 不写业务规则
规则都在 `OrderAggregate.Decide` 和 `OrderAggregate.Evolve`。

### 8.2 Application 只做编排
它负责把 HTTP 世界翻译成领域世界，而不是替代领域决策。

### 8.3 PostgreSQL 是 append-only event store
真正的事实保存在 `event_store`，`order_read_model` 只是投影。

### 8.4 查询既可以走投影，也可以走重放
这个示例两者都展示了：

- `OrderDetailsResponse` 的状态主体来自事件重放
- `Status` / `TotalAmount` / `ReadModelUpdatedAt` 可直接对照投影表结果

---

## 9. 后续怎么继续扩展

如果你要把这个示例正式产品化，下一步建议是：

1. 给 `OrdersController` 增加请求验证器
2. 把 `OrderReadModelProjector` 改成后台异步订阅器
3. 增加订单列表查询接口（直接查 `order_read_model`）
4. 增加快照策略配置化
5. 增加 event upcaster / schema version 兼容
6. 增加集成测试，覆盖真实 PostgreSQL append + projection 流程

---

## 10. 一句话总结

这个例子里，**控制器只接 HTTP，应用层只做编排，核心层只做 Decide/Evolve，基础设施层只负责把事件和投影可靠地落到 PostgreSQL**。这正是事件溯源在这个仓库里的落地方式。
