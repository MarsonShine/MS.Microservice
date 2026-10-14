# 工具 API 接入设计

Lab 将组件的核心能力接成可观察的使用案例，复用已有类库和上传校验，不增加应用项目。文档索引通过共享任务组件和 Coravel 执行；图片批处理保留原有实现。调用和配置见 [Lab README](../README.md#工具组件-api-演示)，算法原理由各组件技术文档维护。

## 接口与宿主边界

每个控制器显式标记 `LabOnly`，由现有 feature provider 控制发现；该标记不继承，不能只放在公共基类上。`ToolDemoControllerBase` 统一身份读取、请求大小和错误响应。持久任务和检索控制器显式要求认证，从身份取得 owner，不接受客户端指定 Scope。新增依赖只进入 Lab 项目，正式 Reference 的依赖图不变。

表单模型用 XML 属性注释说明参数，通过 DataAnnotations 与组件校验约束真实使用。必须上传的文件标记 `Required`；仅视频修复蒙版可选，省略时使用自动模式。浮点参数的 `Range` 使用 double 重载，避免转为整数后错误放行小数边界。

## 文件与执行资源

可选配置的缺失、空字符串和纯空格统一表示未设置。默认值与本地依赖查找集中在 Lab 的 `ToolDemoDefaults`：字体复用组件的平台默认能力，并优先选择已安装的 Windows 中文字体；LibreOffice 只查找宿主 PATH 和标准安装位置。显式路径优先且无效时直接报错，避免掩盖部署错误。模型与专用 Python 环境不能可靠推断，仍由宿主配置；不自动下载或安装依赖。

`ToolDemoFiles` 在读取实际字节时限制上传大小，图片／Excel 同时复用 `FileUploadValidator`；视频和音频在私有临时目录中使用生成文件名，完整格式验证由相应处理器负责。宿主配置决定可执行文件、模型、字体与服务凭据，请求不能提供这些路径或命令。

同步接口在处理完成后读取有界结果，再返回文件字节；工作目录可在 action 结束时清理，下载不依赖随后已删除的文件。清理失败写入日志，保留处理异常或已完成结果。该大小预算不等于进程内存／磁盘硬配额。

本地持久存储复用 `LocalFileStorage` 与 canonical key，owner 单独分目录，S3 使用 owner 命名空间；缺失或属于其他用户的资源统一不可见。ZIP 解压复用 Core 的路径与展开预算保护，然后把文件保存为 owner 的私有 key，复用下载 API。

`ToolInferenceRuntime` 在实例级门控下按需创建模型会话，所有模型类型共用一个门，避免不同推理接口同时争用大量内存。宿主异步释放时等待活跃调用并释放全部已创建会话；OCR 仍遵从组件的每次启动 Python 设计。基础视频、HTML／DOCX 排版各自复用组件原有进程监督，处理期限和取消语义不另行改写。

## 持久任务与索引发布

Lab 引用可选 `Tasks.Dashboard` 并仅在 Lab 端点启用时挂载 `/task-center`。面板与工具 API 使用相同的 OwnerTag Scope，因此同一任务不需要复制或同步。Lab 允许固定空壳匿名加载以输入现有 JWT，数据接口仍须认证；控制请求继续验证框架防伪令牌。计划通过 `UseSchedulerWithTaskDashboard` 注册到同一个 Coravel Scheduler，展示目录由观察器记录，不增加另一套调度生命周期。

`TaskCenterDemo` 为 Lab 注册固定 SQLite 上下文与本地检索实例。数据库初始化 hosted service 先完成，Coravel 使用短 tick 领取持久任务；处理器注册的队列只有一个并发槽。它与已有媒体推理的全局门独立，索引提取调用 OCR 时仍须等待该推理门。数据库 Provider、DDL 和目录选择属于演示宿主；SDK 本身不创建数据库表。

提交接口冻结 owner、上传 key、文档标识、版本及逻辑路径。后台不读取 HttpContext，不把逻辑路径用于磁盘访问，只按已保存的 owner 与 canonical key 打开原文件。Scope 使用与私有存储一致的 owner 哈希；查询、删除和任务状态同样从认证身份构造 Scope。

索引任务先按实际字节有界读取，再调用已有 PDF、DOCX 或 OCR 提取器，最后提交版本变更并显式发布。PDF 有文字层的页面直接使用文字层，空页面才选择 OCR；不在查询期间提取或运行模型。文本不足或超预算的任务失败，异常细节留在宿主日志。取消和任务数据库不能回滚索引提交；安全重试依靠同一文档版本的幂等写入，删除保留更高版本 tombstone，防止延迟任务复活已删除文档。

任务与 Lucene 索引是两个持久存储：处理器发布索引后、任务写成功前发生崩溃，恢复时重复提交相同版本；若新的版本已经生效，旧版本只记 stale。重试不会恢复到更旧文档。索引删除不隐式删除上传文件。任务终态、停止期限和恢复策略见 [执行设计](../../../../src/MS.Microservice.Tasks/docs/execution-design.md)，排序段、快速查询和精确计数见 [检索设计](../../../../src/MS.Microservice.Search/docs/search-design.md)。

## Swagger 与错误

Lab 已生成 `MS.Microservice.Lab.xml`，Swagger 加载该 XML 的 action 摘要、remarks、参数及 DTO 属性注释。Development 的旧 `MS.Microservice.Web.xml` 配置会使注释静默缺失，因此与 Lab 使用同一正确文件名。生成后的 OpenAPI 文档须实际验证，不能仅以源代码中出现注释作为完成依据。

文件 action 的 `Produces` 会影响 ApiExplorer 的错误响应格式；仅在 `ProducesResponseType` 中补错误 MIME 仍不足以纠正生成物。Lab 的 operation filter 根据实际响应类型，只将 `ProblemDetails` 规范为 `application/problem+json`，保留成功文件格式和 JSON 字节字段的 Base64 定义。文件响应声明使用 `FileContentResult`，避免被描述为 Base64 字符串。

可预期的输入和媒体处理错误在公共 filter 映射为参数／格式响应；缺少配置、工具或平台支持返回依赖错误，处理超时单独返回超时响应。原生媒体 stderr 留在服务器日志，不直接暴露本地路径；未识别的服务器错误继续交给宿主管道处理。

## 验证

TestServer 使用测试身份执行真实 MVC 绑定、过滤器和组件调用，验证正常文件下载、识别 JSON、存储与通知闭环，以及 OpenAPI 的摘要、表单、输出和 Lab 隔离。原生媒体／推理／渲染测试显式启用并串行执行，避免抢占资源造成误判；它们验证本机调用链，不代表所有平台或真实云账号验收。SMTP、短信和 S3 需要独立配置后的服务验证，测试不会自动向外发送收费通知。
