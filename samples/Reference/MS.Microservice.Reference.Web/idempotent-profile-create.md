# Profile 与 Order 的重复 HTTP 请求

客户端调用 `POST /api/v1/profiles` 后，档案和 Outbox 可能已经提交，`201` 却在网络中丢失。此时重发普通创建请求，档案唯一约束会阻止第二次写入并返回 `409`；客户端仍不知道第一次创建的档案 ID。`POST /api/v1/orders` 则可能创建第二笔订单。HTTP 幂等记录要解决的是响应丢失后的重试问题，消息 Inbox 不处理客户端的 HTTP 重试。两个接口如何共用执行器，以及 Order 的事务边界，见[Profile 与 Order 如何共用 HTTP 幂等](http-idempotency.md)。

## 两层开关

`Http:Idempotency:Enabled` 默认是 `false`。它只控制宿主是否注册幂等存储和清理任务，以及就绪检查是否要求幂等迁移。接口必须另外标记：Reference 的正式宿主给创建档案路由调用 `.RequireHttpIdempotency("profiles.create")`，给创建订单路由调用 `.RequireHttpIdempotency("orders.create")`。标记在端点映射时包裹该路由的请求委托；没有全局 Middleware 扫描每个请求的 Method 和 Path。MVC 测试宿主使用 Action Attribute，见 [MVC 接入说明](../../../MS.Microservice.Idempotency/src/MS.Microservice.Idempotency.Mvc/README.md)。

| 全局开关 | 路由标记 | `Idempotency-Key` | 结果 |
| --- | --- | --- | --- |
| 关闭 | 任意 | 任意值 | 原业务流程；不解析键，不查幂等表。 |
| 开启 | 未标记 | 任意值 | 原业务流程；不解析键。 |
| 开启 | 已标记 | 无 | 原业务流程；不查幂等表。 |
| 开启 | 已标记 | 有 | 校验键，查询、占位、执行或重放。 |

例如，全局开启后，`PATCH /api/v1/profiles/{id}` 未标记，带 `Idempotency-Key: bad,key` 仍按原来的版本规则更新；它不会因为这个请求头返回幂等键校验错误。全局关闭时，即使创建档案路由已标记，重复创建仍可能因业务唯一约束返回 `409`。关闭状态的请求不查幂等表；就绪检查只忽略两种消息提供者各自已知的 `AddHttpIdempotency` 待迁移报告，`AddOrders` 和其他迁移仍须完成。标准完整迁移仍会建幂等表。

## 请求为什么需要键和指纹

相同 Body 不一定表示重试。客户端可以用相同内容发起两次独立创建，所以服务端先用**操作名、已验证的身份、`Idempotency-Key`**定位记录，再比较请求指纹。同一身份用同一个键提交不同请求，返回 `409`；不同身份不能读取彼此的响应。键只接受一个值，长度为 1–128 个可见 ASCII 字符，不能含逗号。无效键返回 `400`，业务处理不执行。

`ReferenceIdempotencyActorScope` 在宿主启用幂等时注册一次，从认证中间件验证后的 `ClaimsPrincipal` 读取 `iss` 和配置的 subject claim。它只生成隔离记录所需的稳定身份，不检查 `profiles.manage` 等权限；路由的授权策略仍在端点执行前生效。使用整个 token 作身份会让刷新 token 后的重试失去原记录，完全不区分身份则可能把一个用户的响应重放给另一个用户。身份哈希是记录定位的一部分；若更改其生成规则，未清理的记录将无法按原身份和键定位。新增 API 不需要再提供一份身份提取代码。

旧创建档案实现先把已绑定的 `CreateProfile` 再序列化为规范 JSON。新 `ReferenceHttpIdempotencyExecutor` 在模型绑定之前读取并回卷 Body，用 HTTP 层看到的 **Method、Path、QueryString、完整的 Content-Type 和 Body 表示**计算指纹。对 `application/json` 与 `+json` 类型，UTF-8 Body 直接解析，标为 `charset=utf-16` 的 Body 先按 UTF-16 解码；之后递归按字段名排序每层对象。字符串之外的 JSON 空白不参与比较，数组顺序保持不变。非 JSON Body 直接比较原始字节。各部分分别带长度写入 SHA-256，后面的模型绑定仍读取原始 Body。一个 JSON 对象中若字段名重复，即使只差大小写，如 `displayName` 与 `DisplayName`，也会在绑定前返回 `400`，不占用键。

例如，第一次 `POST /api/v1/profiles` 使用键 `K1`、`Content-Type: application/json`，Body 为 `{"issuer":"https://issuer.example","subject":"S","displayName":"first","roles":["reader","editor"]}`，返回 `201` 和 `Location: /api/v1/profiles/P1`。同一身份以 `K1` 重发时，只要 `Content-Type` 不变，即使把对象字段换个顺序、加上换行和空格，仍得到首次保存的 `201`、`Location` 和正文字节，不再创建档案或 Outbox 消息。若把 `roles` 改成 `["editor","reader"]`，或改了查询字符串、`Content-Type` 的大小写或 `charset` 参数，同键返回 `409`。更换 `X-Request-Id` 不影响指纹。非 JSON 请求则须保持 Body 字节完全一致。

Order 使用独立的操作名 `orders.create`，因此也可以使用键 `K1`。第一次提交 `{"sku":"SKU-42","quantity":2}` 返回订单的 `201` 和 `Location`；同键提交 `{"quantity":2,"sku":"SKU-42"}` 重放该响应，不再创建订单。若把 `quantity` 改为 `3`，同键返回 `409`。改用新键提交相同 Body 则是一笔新订单。订单保存创建者的 issuer 和 subject；按 ID 读取时只返回当前身份拥有的订单，其他身份请求该 ID 得到 `404`。这条业务读取规则与幂等键的身份隔离分别生效。

旧 `profiles.create` 记录的请求哈希来自绑定后的 `CreateProfile` JSON，新记录使用 HTTP 字段与排序后的 JSON。旧行不会改写。相同身份和键命中旧记录时，新指纹不同，存储返回 `DifferentRequest`，HTTP 层返回 `409`；不会按旧格式重查、转换记录或执行创建。记录被清理后，同一键才可能重新执行。客户端若仍持有旧键，应改用新键发起新的请求。

## 业务与响应怎样一起提交

首次带键请求查不到记录时，执行器让消息 `IUnitOfWork` 开启外层事务，先插入唯一键占位并刷新到数据库，然后运行原来的端点委托。`ProfileService.CreateAsync` 的内层工作单元加入同一事务，档案和 Outbox 消息与幂等记录使用同一个 `DbContext`。`OrderService.CreateAsync` 也调用同一个作用域中的工作单元；由于外层事务已经活动，内层调用只添加订单，不单独提交。执行器暂存端点实际写出的状态码、`Content-Type`、`Location` 和正文字节，保存成功响应快照后才提交，再把响应送给客户端。因此网络断开在提交之后，也不会使重试再次执行创建。

这也是旧的逐 Action Filter 不合适的原因：它绕过 Action，另写一条创建档案处理路径。修改 Action 的业务规则或响应时，带键路径可能不同步。现在 MVC 的 `IAsyncResourceFilter` 包围模型绑定、Action 和结果写出；Minimal API 则在绑定之前包裹这个路由的请求委托。两者都运行原有业务代码，不再为每个操作复制一份 Filter 和业务处理器。MVC Attribute 目前只负责从 DI 选择共用 Filter；执行器仍放在 Reference.Web，而不是一个可直接套在任意宿主上的通用库。

测试中的另一个 Controller 提供 `/test/mvc/echo`，请求类型是 `JsonElement`，与档案创建无关。它只写 `[RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("echo.create")]`；字段重排会重放，正文改变会返回 `409`。新增 Reference API 仍需实现自己的正常业务处理、选择稳定操作名并标记端点，但不需要新的幂等执行器、Filter 或身份提取代码。换一个宿主时，则需要在宿主层接好自己的存储事务与身份来源一次。

Reference 执行器**只持久化 `2xx` 响应**。创建档案成功保存的是 `201`；MVC 测试还覆盖 `Echo` 的 `200`，以及无正文、无 `Content-Type` 的 `204` 首次响应和重放。业务校验、模型校验或冲突返回 `4xx` 时，执行器回滚占键，再发送该错误；修正请求后可复用该键。异常、取消和 `5xx` 也不留下记录。带键请求的 Body 最多 1 MiB，超过时返回 `413`。响应写入有界暂存流，正文一旦超过 64 KiB 就失败并回滚事务，不会继续把大响应缓存在内存。快照没有保存任意响应头，流式结果、文件下载或依赖其他响应头的接口不应直接标记。

两个请求同时使用同一键时，数据库唯一约束只让一个请求先取得占位。另一个工作单元回滚后，执行器在**新的依赖注入作用域和 DbContext**查询胜者：相同请求重放胜者快照，不同请求返回 `409`。如果没有查到胜者，保留原数据库异常，避免把数据库故障误报为键冲突。事务外的邮件或网络调用不参加这项保证。记录只保存身份和请求键的哈希，不保存原始键；响应正文仍可能包含个人信息，应按业务数据管理。

## 部署和验证范围

部署前，要用独立迁移器应用所选消息提供者的迁移；Reference 宿主不会自动建表。功能开启时，带键请求会查询幂等表；表缺失则请求失败，就绪检查报告待迁移。清理任务每小时检查一次，默认保留 24 小时；未完成迁移时跳过清理。到期但尚未删除的记录仍会重放，删除后同键可能再次执行。关闭功能不会清理已有记录。

两种 Reference `DbContext` 都将幂等表放在 EF 模型中；各自的 `AddHttpIdempotency` 迁移已位于新增的 `AddOrders` 之前。标准完整迁移必须按顺序应用，因此即使全局关闭，也会创建幂等表。关闭只解除请求运行时的依赖，不提供跳过中间迁移而保持无表的部署路径。

SQLite TestServer 测试用例覆盖 Profile 与 Order 的路由选择、JSON 字段重排、响应重放和同键冲突，也覆盖 MVC 多 Action 共用 Filter、旧创建档案记录同键冲突、业务拒绝后复用键、大小限制和事务回滚。其他字符集、真实 PostgreSQL 并发、Wolverine 原生 Outbox 与进程崩溃恢复尚需对应环境验证。本次没有优化前后的耗时、分配或 GC 数据，不能据此声称性能提升；MVC 示例也未验证 Native AOT 发布。
