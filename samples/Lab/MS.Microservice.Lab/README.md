# Laboratory host

This host contains local-identity, file, functional and event-sourcing examples. It does not run inside the production reference host. The default launch profile uses the Lab environment and port 5210.

Supply `LabTokenIssuer__SigningKey` with an externally generated key of at least 32 ASCII characters. `LabTokenIssuer:Issuer`, `Audience` and `LifetimeSeconds` define the active local issuer; validation automatically trusts that active tuple. Optional `IdentityOptions:JwtBearerOption` arrays add trusted issuers/audiences/rotation keys rather than choosing the signing key by array position.

The local account/password login is for this laboratory. Password request Base64 is an encoding, not encryption; company services use the separate reference host's external JWT/OIDC integration. Existing Activation/EventStore practice databases remain separate from reference databases.

`ApiEncryptOptions:IsEnabled` defaults to `false`. When enabled, `/api/lab/encryption/echo` accepts a new RSA-OAEP + AES-GCM envelope for its registered DTO; `/api/lab/encryption/plain` keeps plain JSON through `[NoEncrypt]`. Supply the RSA private key as `ApiEncryptOptions__PrivateKey` from the deployment environment. The binder belongs to the optional AspNetCore.Encryption component; see [加密请求模型绑定](../../../src/MS.Microservice.AspNetCore.Encryption/docs/encrypted-model-binding.md).

可靠消息实验通过 LabMessaging:Enabled=true 显式启用，使用
ConnectionStrings:LabMessagingDatabase 和 Messaging 配置；默认 SelfManaged，也可选择 Wolverine。
使用 Reference.DatabaseMigrator 显式初始化此独立数据库（将 ReferenceDatabase 环境变量指向实验库）。
实验入口为 POST /lab/messaging/profiles、GET /lab/messaging/audit，要求 Lab 身份；
失败查询与重放额外要求 LabMessagingOperations 权限（角色动作路径 lab/messaging/operations）。业务服务和处理器直接复用 Reference 类库，两个 Host 互不引用。

旧 Activation 数据库、Inbox/Outbox 表及迁移历史保留。旧练习保存不再自动产生消息，
旧待发送和死信数据不会自动迁移或派发；需要处置时先备份并人工核对。
新的消息实验与旧练习数据库必须分离。领域事件的跨边界映射见 Reference.Application。

旧实现已从可编译项目移除。早期状态机源码可在提交 2d2273c 查看；它不是当前接入示例。
当前组件回归分别位于 Messaging.SelfManaged.EFCore.Tests、Messaging.RabbitMQ.Tests、
Messaging.Wolverine.Tests。Messaging.IntegrationTests 对两种实现运行相同业务断言；
显式开启 RUN_MESSAGING_INTEGRATION_TESTS=true 时，缺少依赖会失败，不能以跳过代替验收。

## 图片后台批处理

配置 `ImageBatches:Enabled=true` 后，已登录的 Lab 用户可上传一批图片，异步缩放并转换为 PNG／JPEG／WebP。使用现有上传校验、Media 和 Core 本地存储；无需启动另一个服务。

```powershell
curl.exe -X POST http://localhost:5210/api/lab/tools/image-batches `
  -H "Authorization: Bearer $token" `
  -F "Files=@first.png" -F "Files=@second.jpg" `
  -F "Width=1280" -F "Height=720" -F "Format=Webp" -F "Quality=85"
```

创建成功返回 `202`、任务 Id 和状态地址。宽高须同时提供，也可同时省略；默认 `Contain` 等比例适配，另可选 `Cover`／`Stretch`。进度按已完成文件数计算，只有整个批次成功才可下载结果。

JSON 的 `state` 使用枚举数值：`0` 准备、`1` 排队、`2` 处理中、`3` 成功、`4` 失败、`5` 已取消。

| 路径（前缀 `/api/lab/tools/image-batches`） | 用途 |
| --- | --- |
| `POST /` | 上传并创建任务 |
| `GET /{id}` | 查询状态、完成数及输出信息 |
| `POST /{id}/cancel` | 请求取消；运行中的当前步骤可能需要等待 |
| `POST /{id}/retry` | 从保留输入创建新任务，只允许失败或取消的原任务 |
| `GET /{id}/files/{index}` | 下载成功批次的第 index 个文件，从零开始 |
| `DELETE /{id}` | 删除已结束任务及其全部本地文件 |

所有操作按已验证身份的 NameIdentifier 检查所属用户，其他用户和不存在的任务均返回 `404`。进行中任务或正在复制以供重试的任务不能删除。容量已满返回 `429`，先删除已结束记录再提交。

| `ImageBatches` 配置 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 显式启用实验入口 |
| `StorageDirectory` | `.tools/image-batches` | 内容根目录下的可信私有相对目录，不由静态文件中间件公开 |
| `MaximumJobs` / `MaximumFiles` | 16 / 8 | 全局保留记录数（含准备和已结束任务）／每批文件数 |
| `MaximumFileBytes` / `MaximumBatchBytes` | 5 MiB / 20 MiB | 按实际读取字节检查，不仅依赖上传声明长度 |
| `MaximumOutputBytes` | 32 MiB | 每份编码结果上限 |

multipart 请求总上限沿用 25 MiB。图片单边最多 4096、最多 4M 像素，输入和变换后画布均受限；格式签名通过后仍可能因完整解码失败而进入失败状态。质量参数范围 0–100，PNG 的该参数不控制文件大小。

这是单进程、单工作线程的实验队列，**任务元数据重启后丢失**，没有自动重试或跨实例协调。失败或取消的部分结果不可下载，但仍占磁盘，删除任务才会清理；重启遗留目录由宿主管理。需要持久化调度时，应接入已有数据库和 Messaging 组件。流程和状态约束见 [批处理设计](docs/image-batches-design.md)。

演练账号由 Lab.DatabaseMigrator 的 --seed-lab-users 显式创建，并且仅允许 ms_lab_ 前缀数据库。
必须配置 LabBootstrap:OperatorPassword 和 ReaderPassword（至少 16 字符）。
lab-operator 获得消息运维动作，lab-reader 没有此动作；密码使用 PasswordHasher 存储。
重复执行不会覆盖已有账号的密码或权限。正式 Reference Host 没有此初始化入口。
