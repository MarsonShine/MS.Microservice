# Core：通用代码辅助

Core 提供跨业务可复用的函数式结果、规格、集合、序列化、HTTP 等辅助能力。
它依赖 Domain.Primitives 的小契约，不依赖 Reference、Lab、具体数据库或业务 User/Order 模型。

它不是所有基础设施的总入口。消息、AI、日志和 ORM 有各自模块；
只有确实需要这些通用类型的项目才引用 Core，不能为了目录“统一”制造额外依赖。

## 阅读思路

- Functional 中的 Option/Either/Result 表达缺失值和业务失败，避免把所有分支都变成异常控制流。
- Specification 表达可复用查询意图；数据库 Provider 的翻译与执行属于持久化模块。
- Net/Http 负责请求编码、生命周期和异常语义，不能替调用方决定业务重试或授权。
- Domain.Primitives 中的实体/审计契约可单独使用；具体聚合根与规则属于应用 Domain。

公共契约的注释说明用法与限制，非直观分支附近说明原因。
更高层取舍见[总体架构](../../docs/Architecture-Overview.md)；
复制或包引用需要包含声明的依赖，见[组件消费](../../docs/components/consumption.md)。

## Core 裁剪与 AOT 分析

Core 在项目中设置 `IsAotCompatible=true`，日常构建即启用裁剪、AOT 和单文件兼容性分析，
并允许消费应用裁剪未使用的 Core 代码。它不是关闭警告的开关，也不能代替实际发布。
支持用法仍要求调用方提供业务类型的 JSON 元数据和查询映射；外部 Provider 的兼容性由对应模块验证。

在仓库根目录运行，仅检查 Core 及构建所需的项目引用：

```powershell
./build/validate-static-aot.ps1 -Project src/MS.Microservice.Core/MS.Microservice.Core.csproj
```

脚本使用相同的裁剪/AOT 分析参数还原并重新编译，确认 Core 自身的编译命令加载
`ILLink.RoslynAnalyzer.dll`。还原或构建失败、未加载分析器、出现 IL 诊断均视为失败。
日志及成功报告位于 `artifacts/aot/MS.Microservice.Core`；失败重跑会清除旧的成功报告。
默认使用仓库 SDK 对应的分析器版本，不需要指定 `ILLinkVersion`。

这是静态分析基线，不发布或运行 Native AOT 程序，也不代表第三方依赖的完整原生兼容性。
普通构建成功或 JSON 反射关闭测试通过，均不能替代此检查。

分析脚本自身的失败分支回归检查：`./build/test-validate-static-aot.ps1`。
该检查使用隔离的模拟项目与 dotnet 调用，不访问网络，也不替代上述真实分析。

## Core 原生消费验证

在 Windows x64 或 Linux x64 主机上运行：

```powershell
./build/validate-core-native-aot.ps1
```

默认依次发布并运行两种模式。`Analysis` 将 Core 作为 `TrimmerRootAssembly`，扩大库及其依赖调用的分析范围；
`Consumer` 不额外保留程序集，检查实际消费路径的裁剪结果。可用 `-Mode Analysis` 或 `-Mode Consumer` 单独运行。
每次运行、每种模式均使用独立的构建和输出目录，日志及全部成功后的报告保存在
`artifacts/aot/Core.Native/<RID>/<运行标识>`。任何发布失败、IL 诊断或程序非零退出码都会使检查失败。

Windows 需要 Visual Studio 的 C++ 桌面开发工具链及 Windows SDK；Ubuntu 需要 clang 和 zlib1g-dev。
目标平台须与运行主机一致，不支持通过 Windows 交叉发布来代替 Linux 验证。

验证程序直接消费 Core，不使用 xUnit 或动态 Mock。模型与生成上下文由消费程序定义，
HTTP 使用内存 Handler，缓存使用内存替身，不连接外部服务。
脚本将发布的可执行文件单独复制到空目录运行，不携带托管 DLL 或 runtimeconfig。
`PublishAot` 会影响中间托管程序的运行时开关，因此单独运行 DLL、`dotnet run` 或仅检查动态代码开关不构成原生验证。
完整库分析不等于执行所有 API，也不覆盖所有泛型实例。当前 JSON、HTTP、缓存的场景见
[SerializationScenarios.cs](../../test/MS.Microservice.Core.NativeAot.Smoke/SerializationScenarios.cs)：

- HTTP 检查闭合泛型、集合、空值、自定义转换器、多态、中文编码、请求头隔离，以及失败和取消。
- 元数据缺失时，同时检查异常与请求次数，确保请求没有发出；也验证合法的嵌套类型可以发送。
- 查询映射在 `fr-FR` 区域性下检查小数、日期、重复参数、空值，以及已有查询串和片段。
- 缓存检查命中与回源次数、空结果不写入、过期配置、UTF-8 BOM、坏数据和取消。

现有 `MS.Microservice.Aot.Tests` 通过源码链接运行同一组场景，便于比较托管与原生结果；
原生程序只编译场景和手写替身，不引入 xUnit。这里验证的是 Core 的编码、元数据和异常行为，
不验证真实网络或缓存服务的过期机制。

基础能力见 [FoundationScenarios.cs](../../test/MS.Microservice.Core.NativeAot.Smoke/FoundationScenarios.cs)，
Reactive 路径见 [ReactiveScenarios.cs](../../test/MS.Microservice.Core.NativeAot.Smoke/ReactiveScenarios.cs)。
两者也由托管测试和原生入口共用：

- 类型与实体键检查值类型、可空类型、引用类型和复合键，并区分默认值、临时键与已赋值的键。
- 事件登记检查引用类型和值类型事件、顺序等待、退订和异常传播；不验证并发修改订阅集合。
- 配置使用真实 Microsoft DI 容器，检查服务构造、单例生命周期和布尔值解析。消费程序须先注册 `IConfiguration`；
  `AddFeatureToggle` 只登记功能开关服务。容器实现是验证程序的依赖，没有加入 Core。
- 表达式只检查组合结构、参数重绑定、排序、投影和 Include 的泛型分派。
  没有调用 `Compile`、`AsQueryable`，也没有连接 ORM；这些结果不证明任何数据库 Provider 能翻译或执行表达式。
- Reactive 使用现有 System.Reactive 依赖，检查筛选、合并、异步成功/失败、Trace 订阅释放，以及热流的相邻配对。
  异步场景用任务信号驱动，十秒超时只用于结束挂起的测试。没有据此承诺冷流的配对语义、多个订阅只执行一次副作用，
  或任意调度器下的并发行为。

这组验证的判断依据是消费方能否在原生产物中构造类型、调用代码并得到约定结果。
它不把出现 `typeof`、泛型或表达式树本身视为不兼容，也不覆盖 System.Reactive 的全部 API。

## CI 如何判定通过

[dotnet-ci.yml](../../.github/workflows/dotnet-ci.yml) 中的 `Core AOT` 作业只验证 Core 及其依赖，
分别在 Windows Server 2025 x64 和 Ubuntu 24.04 x64 上运行。SDK 来自仓库的 `global.json`，
Linux 作业补齐 clang 和 zlib 开发包，Windows 使用 runner 的 C++ 工具链。

每个平台依次执行脚本回归、Core 静态分析、Core 测试、关闭 JSON 默认反射的 AOT 测试，
最后发布并运行 Analysis 和 Consumer 两种原生产物。任一检查失败，该平台作业就失败；
一个平台失败不会取消另一个平台，便于同时看到两边的结果。

日志以 `core-aot-win-x64` 和 `core-aot-linux-x64` 附件保留七天，失败时也会尝试上传。
附件包含编译日志、原生运行日志、成功报告和测试结果，不打包构建目录或原生二进制。
如果发布或运行中途失败，可能只有日志，没有 `results.json`；缺少报告不代表通过。

两个平台的 CI 都通过，才说明当前提交在这两个环境中通过了已有场景。
本机验证、工作流配置检查和 GitHub runner 上的实际运行是不同的证据，不能互相替代。

## JSON 元数据由谁提供

`LogHttpClient` 从 `JsonTypeRegistry` 查找请求的实际类型和响应的声明类型。
缓存与 `MS.WebHttpClient` 扩展直接接收 `JsonTypeInfo<T>`。调用方通过生成上下文提供这些元数据。

Registry 只登记根类型，不会合并或补全每份元数据内部的解析器。例如 `Envelope.Data` 声明为 `object`，
即使已经把 `Item` 登记到 Registry，Envelope 所用上下文仍须知道 Item，才能序列化这个嵌套值。
普通 object 属性反序列化后通常得到 `JsonElement`；需要恢复具体派生类型时，应声明多态契约。
缺失契约会报错，没有自动反射回退。`DefaultSerializeSetting` 只提供 JSON 配置，不提供业务类型元数据。

### HTTP 请求辅助

LogHttpClient 的 GET 参数使用 `IDictionary`，或使用业务对象与显式的 `QueryParameterMap<T>`；不会自动扫描对象属性。
null 值省略，空字符串保留，
集合展开为同名参数，数值使用 invariant culture，日期使用往返格式。键和值分别 URL 编码，
追加参数时保留已有查询串和片段。POST 使用 UTF-8 application/json。
传入的请求头仅属于本次请求，不修改 HttpClient.DefaultRequestHeaders。

LogHttpClient 的取消、HTTP 状态失败和 JSON 解析失败分别保留 OperationCanceledException、
HttpRequestException、JsonException；调用方应更新旧的“统一解析异常”捕获逻辑。
默认日志不记录 URL、参数、正文及异常消息。

较早的 `MS.WebHttpClient.HttpClientExtensions` 仍在非成功状态或无效 JSON 时返回默认值；
它与 LogHttpClient 的异常约定不同，不能按同一套失败规则替换调用。
