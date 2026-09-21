# 代码级性能与静态元数据迁移

专项分支为 `codex/performance-aot`，基线为 `master@7b1c78a`。范围是具体方法中的计算、分配、阻塞和动态成员发现；没有调整数据库、缓存策略、消息架构或业务流程。除下述 Excel 兼容性调整外，公开动态接口已按计划破坏兼容，仓库调用方同步迁移，未保留隐式反射回退。

Excel 后续调整：旧命名空间和接口已恢复，静态实现迁入 `src/MS.Microservice.Excel.Aot`。仍只有一个 Excel 项目，通过 ExcelVariant 分别打旧版与 AOT 包；AOT 入口没有动态回退。详见 [Excel 目录与打包说明](../src/MS.Microservice.Excel.Aot/README.md)。

## API 迁移

| 原入口或写法 | 现在的调用方式 | 行为边界 |
|---|---|---|
| 自动读取查询对象属性 | `QueryParameterMap<T>` 显式列出有序名称与 getter | 字典入口保留；读取最新值，null 跳过，集合展开、不变文化及 URI 转义保持 |
| Excel 根据属性和注解发现模型 | 旧接口保留；新 `MS.Microservice.Excel.Aot` 使用 `ExcelModelMap<T>` | 两套命名空间隔离，同一项目按 ExcelVariant 分别出包 |
| `Activator` 求默认值 | `TypeHelper.IsDefaultValue<T>` / `GetDefaultValue<T>` | 按 default 语义，不执行自定义结构体构造函数 |
| 装箱实体键默认值 | `IsDefaultBoxedValue`；自定义键先 `RegisterDefaultValue<T>()` | 保留 int/long 非正临时键语义；未知值类型明确失败 |
| `LogHttpClient(logger, http)` | 构造时加 `JsonTypeRegistry` | 请求/响应根类型显式登记；未知类型失败；匿名请求改为命名 DTO |
| JSON HTTP 扩展与缓存 | 调用时传 `JsonTypeInfo<T>` | 上下文决定大小写、命名、枚举、null、编码；没有默认解析器兜底 |
| `MessageContract.For<T>(name, version)` | `For<T>(name, generated.T, version)` | 合同名、版本和信封身份校验保持；原线协议使用 Web options |
| AI 聊天/媒体协议 | 内置命名 DTO 与生成上下文 | 字段名、流式分片、错误响应及省略规则保持 |
| 宿主题目响应模型 | 使用严格 options 创建生成上下文，再 `AddJsonTypeInfo(...)` | 未知根类型失败；额外字段、宽松数字等仍拒绝 |
| SqlSugar 任意类型 JSON | `SqlSugarSerializeService(params JsonTypeInfo[])` | 登记根类型与其成员；ObjectJsonConverter 明确注入该服务 |
| EF 模型扫描后反射注册 | `modelBuilder.Entity<T>().AddSoftDeletedQueryFilter()` | 每个软删除实体显式登记；保持过滤表达式和 DeletedAt 索引 |
| Wolverine 动态桥接 | `WolverineMessageRegistration<TContext>.For<TEvent>()` | 配置入口要求覆盖全部合同的注册列表 |
| 配置/Provider 反射读取 | 具体类型绑定生成器、静态选择器 | Core 单个布尔开关使用标量解析；配置失败和默认值有回归测试 |
| ExpressionStarter 隐式 Func / Compile | 查询保留表达式树，内存筛选直接声明 `Func<T,bool>` | 不在生产类内替调用方编译或退回解释器 |
| Core PropertyAccessors | 已退出 Core | Excel 兼容构建恢复旧接口及局部成员访问逻辑；MiniExcel 只进入 Legacy/All，不进入 Aot 包 |

元数据通过标准 `System.Text.Json` Source Generator 生成。显式接口不会禁止调用方主动传入自定义转换器或反射生成的元数据，但组件自身不补充默认反射解析器。宿主应提供生成上下文，并在禁用默认 JSON 反射的测试中验证自己的契约。

## 提交切片

全部计划项已完成。后续修正使用独立提交，未 amend、rebase 或 squash 已交付历史。按编号阅读便于对照计划，实际分支提交顺序以 Git 为准。

| 切片 | 提交 | 交付内容 |
|---|---|---|
| P01 | `c18b753` | 内置原地洗牌和有限错位修复 |
| P02 | `76f4605` | 转换源只枚举一次 |
| P03 | `990a2e9` | 正文日志异步，关闭时透传 |
| P04 | `52abd2d` | 复用 JSON 配置和编码器，直接解析缓存字节 |
| P05 | `5baccc8` | 私有共享零块写静音，去除 ref 缓冲参数 |
| A01 | `931928a` | 有序查询映射和两个 HTTP 入口迁移 |
| A02 | `3f6274a` | Excel 显式模型映射 |
| A03 | `8e2c77c` | 模板映射和静态颜色表 |
| A04 | `4166d0b` | 泛型默认值和登记的装箱比较 |
| A05 | `f221005` | Core HTTP / 缓存要求 JSON 元数据 |
| A06 | `bdb05da` | 消息 JSON 契约静态登记 |
| A07 | `c6fbe3d` | 聊天及错误协议生成元数据 |
| A08 | `fe742a3` | 媒体请求命名 DTO 和生成元数据 |
| A09 | `ff95c03` | 题目严格 JSON、schema 和比较使用登记元数据 |
| A10 | `eb11a6c` | SqlSugar 显式 JSON 元数据 |
| A11 | `df9a74c` | 泛型软删除过滤器 |
| A12 | `88082be` | Wolverine 静态桥接登记 |
| A13 | `e151477`、`25040e0`、`ac6c6c9` | AI / 平台生成绑定、静态 Provider 选择与标量开关 |
| A14 | `9acb517`、`e640656`、`1a911d1` | 动态访问器、隐式编译和旧 Excel 入口退役 |
| A09/A13 修正 | `31a9438` | schema 节点重载及 DI 构造函数裁剪契约 |
| A06/A12 修正 | `f7427fb` | 处理器构造函数保留链和消费探针迁移 |
| D01 | 包含本文的提交 | 学习索引、迁移说明及最终验证脚本 |

每片的机制、成本、局部替代和验证边界见[学习索引](../samples/Lab/MS.Microservice.Lab.AotExamples/README.md)。Legacy 保留完整旧文件或可运行旧机制，Static 独立实现核心新写法；旧无限循环算法只使用有界探针。学习类库 `IsPackable=false`，生产项目不引用它。

## 复核命令

```powershell
dotnet restore MS.Microservice.slnx
dotnet test MS.Microservice.slnx --no-restore --logger trx --results-directory artifacts/final-tests
./build/validate-static-aot.ps1
```

分析脚本只执行 restore 与带裁剪/AOT 分析器的 build，不发布、不启动原生可执行文件。它检查实际编译命令含 `ILLink.RoslynAnalyzer.dll`，任何 IL 诊断或缺失分析器都失败；日志与结果在 `artifacts/aot`。分析范围为根 src、AI、Messaging、Persistence 的 21 个生产项目，自有源码与生成代码受检，第三方已编译程序集不会被 Roslyn 深入分析。Excel 现在明确选择 Aot 模式；Legacy/All 包含有意保留的动态兼容代码，不在零诊断承诺内。

本机 SDK 为 10.0.401。缓存只有 ILLink 10.0.11，而 SDK 默认选择 10.0.12，本次显式选择缓存版本运行：

```powershell
./build/validate-static-aot.ps1 -ILLinkVersion 10.0.11 `
    -PackageSource C:/Users/shuai.mao/.nuget/packages `
    -PackagesPath C:/Users/shuai.mao/.nuget/packages
```

版本覆盖只在 artifacts 下生成临时 MSBuild hook，不修改仓库 SDK 或依赖版本。不提供参数时使用 SDK 默认版本。离线单元测试的 restore 也可使用同样的 `--source` / `--packages` 路径。

## 实际验证结果（2026-09-21）

以下全量结果对应原迁移收尾提交 `34a410a`。完整解决方案 `dotnet test MS.Microservice.slnx --no-restore` 成功，33 个测试项目共 2100 项通过、0 项失败、31 项跳过。按 TRX 的实际 outcome 统计，跳过项为 Excel 的 19 项 opt-in 性能基准和消息集成的 12 项外部 PostgreSQL/RabbitMQ 检查。

主要相关结果：Core 847、关闭默认 JSON 反射的 Core 专项 44、学习对照 80、Lab 284、Excel 38、Audio 58、AI 合计 164、消息组件及本地探针合计 112、Persistence 35、AspNetCore 20、Observability 15。完整方案还覆盖 Reference 宿主、迁移器和基础组件。测试同时构建所有调用方；没有遗留签名已修改却无法编译的消费示例。

21 个生产项目的裁剪/AOT Roslyn 分析全部成功，每个编译命令均确认加载 ILLink 10.0.11，零 IL 诊断。可复用脚本的实际运行结果保存在 `artifacts/aot/results.json`，逐项目日志包含编译器参数；这是静态分析结果，不是 NativeAOT 发布结果。

## 验证边界

Core 专项测试项目关闭 `JsonSerializerIsReflectionEnabledByDefault`，真实运行 HTTP、缓存和未知类型失败路径；AI、消息抽象和 SqlSugar 的相应测试同样禁用默认 JSON 反射。原 Core 通用测试仍允许反射，供历史 JSON converter 测试使用。学习区也有意允许 Legacy 动态实现运行。

配置生成器不仅检查 csproj 开关，还输出并核对了具体类型的绑定方法与 interceptor。启动/重载时的配置绑定和一次性模型登记归为 **AOT compatibility**，不把它们描述成每次请求都反射的高频损耗。性能项通过完成条件、枚举次数、缓冲大小、真实协议和预热分配断言验证，不以总体吞吐量提升作未经测量的结论。

NPOI、EF、Wolverine、SqlSugar 及 Web 宿主的完整 NativeAOT 兼容性未通过发布验证，本次也没有执行发布。外部 PostgreSQL/RabbitMQ 恢复测试和 Excel 大型性能基准遵循现有 opt-in 开关；跳过项必须单独列出，不能算作通过。

## Excel 兼容性后续验证

恢复旧接口并隔离 AOT 源码后，运行 Excel 测试 67 项、Lab 测试 284 项、架构边界测试 39 项，全部通过；Excel 原有 19 项 opt-in 性能基准仍跳过。旧测试现在运行真实旧实现，新旧实现直接交叉读取工作簿。

已分别生成 Legacy 与 Aot 两个本地 NuGet 包，检查程序集命名空间和 nuspec 依赖：Aot 包没有旧类型或 MiniExcel。All 模式直接打包会被拒绝。源码导出包含两个目录，导出后的单项目解决方案独立 restore/build 成功。21 个静态分析目标重新验证通过，其中 Excel 明确选择 Aot 构建。没有新增项目，没有向远程源发布包，没有执行 NativeAOT publish。
