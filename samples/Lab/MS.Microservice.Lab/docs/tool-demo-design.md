# 工具 API 接入设计

Lab 将组件的核心能力接成可观察的使用案例，复用已有类库和上传校验，不增加应用项目或另一套任务系统。调用和配置见 [Lab README](../README.md#工具组件-api-演示)，算法原理由各组件技术文档维护。

## 接口与宿主边界

每个控制器显式标记 `LabOnly`，由现有 feature provider 控制发现；该标记不继承，不能只放在公共基类上。`ToolDemoControllerBase` 统一鉴权、请求大小和错误响应，持久文件／通知从已验证身份取得 owner，不接受客户端指定 owner。新增依赖只进入 Lab 项目，正式 Reference 的依赖图不变。

表单模型用 XML 属性注释说明参数，通过 DataAnnotations 与组件校验约束真实使用。必须上传的文件标记 `Required`；仅视频修复蒙版可选，省略时使用自动模式。浮点参数的 `Range` 使用 double 重载，避免转为整数后错误放行小数边界。

## 文件与执行资源

`ToolDemoFiles` 在读取实际字节时限制上传大小，图片／Excel 同时复用 `FileUploadValidator`；视频和音频在私有临时目录中使用生成文件名，完整格式验证由相应处理器负责。宿主配置决定可执行文件、模型、字体与服务凭据，请求不能提供这些路径或命令。

同步接口在处理完成后读取有界结果，再返回文件字节；工作目录可在 action 结束时清理，下载不依赖随后已删除的文件。清理失败写入日志，保留处理异常或已完成结果。该大小预算不等于进程内存／磁盘硬配额。

本地持久存储复用 `LocalFileStorage` 与 canonical key，owner 单独分目录，S3 使用 owner 命名空间；缺失或属于其他用户的资源统一不可见。ZIP 解压复用 Core 的路径与展开预算保护，然后把文件保存为 owner 的私有 key，复用下载 API。

`ToolInferenceRuntime` 在实例级门控下按需创建模型会话，所有模型类型共用一个门，避免不同推理接口同时争用大量内存。宿主异步释放时等待活跃调用并释放全部已创建会话；OCR 仍遵从组件的每次启动 Python 设计。基础视频、HTML／DOCX 排版各自复用组件原有进程监督，处理期限和取消语义不另行改写。

## Swagger 与错误

Lab 已生成 `MS.Microservice.Lab.xml`，Swagger 加载该 XML 的 action 摘要、remarks、参数及 DTO 属性注释。Development 的旧 `MS.Microservice.Web.xml` 配置会使注释静默缺失，因此与 Lab 使用同一正确文件名。生成后的 OpenAPI 文档须实际验证，不能仅以源代码中出现注释作为完成依据。

文件 action 的 `Produces` 会影响 ApiExplorer 的错误响应格式；仅在 `ProducesResponseType` 中补错误 MIME 仍不足以纠正生成物。Lab 的 operation filter 根据实际响应类型，只将 `ProblemDetails` 规范为 `application/problem+json`，保留成功文件格式和 JSON 字节字段的 Base64 定义。文件响应声明使用 `FileContentResult`，避免被描述为 Base64 字符串。

可预期的输入和媒体处理错误在公共 filter 映射为参数／格式响应；缺少配置、工具或平台支持返回依赖错误，处理超时单独返回超时响应。原生媒体 stderr 留在服务器日志，不直接暴露本地路径；未识别的服务器错误继续交给宿主管道处理。

## 验证

TestServer 使用测试身份执行真实 MVC 绑定、过滤器和组件调用，验证正常文件下载、识别 JSON、存储与通知闭环，以及 OpenAPI 的摘要、表单、输出和 Lab 隔离。原生媒体／推理／渲染测试显式启用并串行执行，避免抢占资源造成误判；它们验证本机调用链，不代表所有平台或真实云账号验收。SMTP、短信和 S3 需要独立配置后的服务验证，测试不会自动向外发送收费通知。
