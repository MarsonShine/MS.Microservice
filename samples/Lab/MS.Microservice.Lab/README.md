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

## 工具组件 API 演示

工具接口位于 `/api/lab/tools`，只在 Lab 宿主提供。启动后打开 `http://localhost:5210/swagger`，按现有 Lab 登录流程获取 JWT，在 Swagger 的 Authorize 中填写令牌，再使用 Try it out 上传文件或提交 JSON。

接口演示组件的正常使用：图片、音频、视频和文档处理直接下载结果，识别和查询返回 JSON。参数含义、默认值、枚举数值、输出类型及运行前提均写在 Swagger 中。这里不提供故障注入或边界用例接口。

| 路由分组 | 可体验的核心功能 |
| --- | --- |
| `/images` | 裁剪、旋转翻转、缩放、转格式、纯色背景、文字／Logo 水印 |
| `/codes` | 生成二维码和条码 PNG，上传图片识别内容与定位点 |
| `/videos` | 信息读取、编辑转码、有序拼接、截帧、提取音轨、带时间范围的文字／Logo 水印 |
| `/inference` | OCR、带线框表格、自动图片去水印、蒙版修复、抠图、两倍增强、自动／手动视频修复 |
| `/audio` | 信息读取、剪辑、音量与淡变、合并、混音、转码和单声道转立体声 |
| `/spreadsheets` | Excel 示例与导出、带定位错误的导入报告、错误行下载，CSV 读写 |
| `/documents` | PDF 信息、文字、选页重排、合并拆分、PNG 渲染、水印叠加；Word 模板下载与填充 |
| `/rendering` | HTML／CSS 排版为 PDF，可信普通 DOCX 转 PDF |
| `/files` | 私有文件保存、下载、元数据、删除、SHA-256、重复候选、ZIP 打包解包 |
| `/text` | 精确逐行比较、指定编码转换、JSON 格式化与压缩 |
| `/notifications` | 模板预览、SMTP 邮件、阿里云模板短信、本人站内通知发布／查询／已读 |
| `/s3` | 本人命名空间下条件写入、查询、下载、删除和临时 GET 地址 |
| `/image-batches` | 复用下文已有后台队列，查看批量处理进度、取消、重试与下载 |

可以按以下顺序查看实际效果：

- **Excel／CSV**：先下载 `/spreadsheets/excel/sample` 或调用导出，再上传到 `excel/import-report`；CSV 用 `csv/write` 生成后再 `csv/read`。
- **Word → PDF**：下载 `/documents/word/template-sample`，到 `word/fill` 上传模板并填入 Tag 字典，最后把结果上传到 `/rendering/docx-pdf`。
- **文件与 ZIP**：上传后保留返回的 key 和下载地址，再查询摘要、比较重复候选或删除；ZIP 解包返回的每个文件也有独立下载地址。
- **媒体**：先编辑或加水印，再使用 `/inference/watermarks/remove` 下载结果、蒙版和报告，检查误选后用 `repair` 上传修正蒙版。视频修复下载 FFV1 MKV 预览包，需要 MP4 时再调用视频转码。
- **通知**：先渲染模板或发布一条本人站内通知；配置邮件／短信服务后再主动调用发送接口，发送会产生真实邮件或短信。

例如上传图片并下载 WebP：

```powershell
curl.exe -X POST http://localhost:5210/api/lab/tools/images/process `
  -H "Authorization: Bearer $token" `
  -F "File=@photo.png;type=image/png" `
  -F "Width=800" -F "Height=600" -F "Format=2" `
  -o resized.webp
```

### 运行前提与配置

`appsettings.Lab.json` 的 `ToolDemos` 提供配置位置；凭据通过环境变量或用户机密提供，例如 `ToolDemos__Smtp__Password`。模型和 Python 路径须为绝对路径，模型文件及依赖须提前部署；接口不会下载模型或安装环境。改变已加载模型的配置后应重启宿主。

| 功能 | 必要配置／环境 |
| --- | --- |
| 图片文字水印、视频文字水印 | `FontFilePath` 指向包含目标字形的字体文件 |
| 视频基础处理 | `FfmpegExecutablePath`／`FfprobeExecutablePath`，默认使用 PATH 中的 `ffmpeg`／`ffprobe` |
| OCR、表格 | `PythonExecutablePath`、`OcrModelDirectory`；[OCR 依赖与模型清单](../../../MS.Microservice.Media/src/MS.Microservice.Media.Inference/README.md#图片文字识别) |
| 图片自动去水印 | Python、`MiganModelPath`、`WatermarkDetectorModelPath` 及检测依赖；手动图片修复只需 MI-GAN |
| 视频去水印／修复 | Python、FFmpeg、MI-GAN 和视频依赖；自动模式另需水印检测模型 |
| 抠图、两倍增强 | `BackgroundRemovalModelPath`／`SuperResolutionModelPath`；使用 .NET 推理，无需 Python |
| HTML → PDF | Windows，已安装 Edge／Chrome；`HtmlBrowserChannel` 默认 `MicrosoftEdge`，也可指定 `HtmlExecutablePath` |
| DOCX → PDF | Windows，`LibreOfficeExecutablePath` 指向完整 LibreOffice 的 `program/soffice.com` |
| SMTP、短信、S3 | 分别配置 `Smtp`、`Sms`、`S3` 子节并显式设置 `Enabled=true`；密钥不作为 API 参数传入 |

缺少配置的功能返回 `503` 和对应配置键，其他工具仍可使用。PDF PNG 渲染依赖部署包内的 PDFium 原生库；部分音频编解码依赖 Windows 或原生 LAME。完整组件限制见 [Audio](../../../src/MS.Microservice.Audio/README.md)、[Documents／Rendering](../../../src/MS.Microservice.Documents.Rendering/README.md) 和 [Inference](../../../MS.Microservice.Media/src/MS.Microservice.Media.Inference/README.md)。

同步演示统一限制每份上传 20 MiB、请求合计 25 MiB、下载结果 64 MiB；具体画布、页数等以各字段说明为准。媒体推理全局串行，原生模型按需加载并复用；抠图实测单任务峰值约 6.4 GiB，宿主仍须留足余量。自动识别、去水印、抠图和增强均需人工查看效果，不能据此证明原始细节或保证识别正确。

私有文件与站内通知按当前用户隔离，其他用户的 key 不可访问；文件默认保存在内容根目录的 `.tools/tool-demo-files`，可通过 DELETE 清理。临时处理结果在读取完成后清理，清理失败记录到宿主日志；持久文件的容量与保留期由宿主管理。SMTP／短信成功只表示服务商接受，S3 写入异常不代表远端未提交，应使用已知 key 核对，避免盲目重试。

接口组织、资源所有权与 Swagger 验证见 [工具 API 接入设计](docs/tool-demo-design.md)。

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
