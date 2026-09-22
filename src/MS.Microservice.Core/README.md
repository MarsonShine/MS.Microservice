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

验证程序直接消费 Core，不使用 xUnit 或动态 Mock。当前验证原生运行条件、关闭 JSON 默认反射，
以及消费方定义的模型和生成上下文通过 `JsonTypeRegistry` 完成序列化往返。
脚本将发布的可执行文件单独复制到空目录运行，不携带托管 DLL 或 runtimeconfig。
`PublishAot` 会影响中间托管程序的运行时开关，因此单独运行 DLL、`dotnet run` 或仅检查动态代码开关不构成原生验证。
完整库分析不等于执行所有 API，也不覆盖所有泛型实例；此入口尚不代表 HTTP、缓存、Reactive 等能力的完整场景验证。

### HTTP 请求辅助

LogHttpClient 的 GET 参数可为公开可读属性对象或 IDictionary；null 值省略，空字符串保留，
集合展开为同名参数，数值使用 invariant culture，日期使用往返格式。键和值分别 URL 编码，
追加参数时保留已有查询串和片段。POST 使用 UTF-8 application/json。
传入的请求头仅属于本次请求，不修改 HttpClient.DefaultRequestHeaders。

取消、HTTP 状态失败和 JSON 解析失败分别保留 OperationCanceledException、
HttpRequestException、JsonException；调用方应更新旧的“统一解析异常”捕获逻辑。
默认日志不记录 URL、参数、正文及异常消息。
