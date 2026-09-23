# `src` 基础设施项目性能检查（2026-09-23）

本轮按 Git 跟踪的九个 `src/MS.Microservice.*` 项目逐一检查。修改前先用现有调用路径和测试判断是否有可测的热点；只保留有对比数据、能维持原语义的局部改动。测量环境为 Windows x64、.NET 10.0.12、Release。微基准测单个方法，不代表生产吞吐或尾延迟。

| 项目 | 本轮结论 | 证据或原因 |
| --- | --- | --- |
| AspNetCore | 提交 `0c4b8ca` | 授权权限检查分割字符串，单 claim 匹配由 342.8 ns / 344 B 降到 259.9 ns / 216 B；详见[项目说明](../src/MS.Microservice.AspNetCore/README.md#permission-check-cost)。 |
| Audio | 提交 `6dec9ab` | 未知格式探测由 570.0 ns / 1160 B 降到 505.3 ns / 0 B；同时修正大于 2 GiB 的流长度先转 `int` 的溢出；详见[项目说明](../src/MS.Microservice.Audio/README.md)。 |
| Core | 不改动 | 已使用的 HTTP 查询参数对象路径依赖 `PropertyAccessors` 运行时编译并缓存属性访问器。它已避免每次调用重复反射，但 IL 和表达树两种策略都不适用于 Native AOT。仅在这里换成另一种反射写法既不解决 AOT，也缺少收益证据。Core 测试 785 项通过。 |
| Domain.Primitives | 不改动 | 只有实体、审计等契约，没有本程序集拥有的高频执行路径。作为 Core 依赖完成构建。 |
| EventBus | 不改动 | 订阅管理器用字典和集合查找，并用锁保护更新；没有定位到需要替换的数据处理热点。组件测试 18 项通过。 |
| EventSourcing | 不改动 | 项目定义事件封装、快照和存储接口；实际存储与投影在调用方项目。相关 Lab 测试 14 项通过。 |
| Excel | 不改动 | 现有基准在 1000 行 × 5 列上显示导入中位数 32.2 ms、导出 66.8 ms。诊断阶段中，导入 `import-title-map` 约 26.53 ms / 5.13 MB，导出写出约 31.36 ms / 4.25 MB。前者包含首次读取工作表行；目前不能分辨 NPOI 延迟物化与本项目映射成本，不能据此改写映射。常规测试 25 项通过，19 项性能测试默认跳过，另选跑两项小规模性能测试通过。 |
| Observability | 提交 `bf0cb7b` | 固定 `outcome` 标签原来每次记录分配 40 B；复用标签后四个基准场景均为 0 B/次；详见[项目说明](../src/MS.Microservice.Observability/README.md#时长指标的标签分配)。 |
| Swagger | 不改动 | `Assembly.GetEntryAssembly()` 只在配置 Swagger XML 注释文件路径时调用，没有出现在请求处理路径；没有足够的性能收益依据去修改。组件测试 8 项通过。 |

Excel 的阶段数据来自 `MS_MICROSERVICE_RUN_PERF_TESTS=1` 且 `EnableOfficeDiagnostics=true` 的 `PerformanceBaselineTests.Export_Obj_Narrow_Small` 和 `Import_Obj_Narrow_Small`，每项预热两轮、测量五轮，测试在每轮前强制 GC。它适合定位候选阶段，不能当作生产吞吐。诊断关闭时的同两项测试也通过；两种构建的耗时不能直接当成优化前后数据。

## 后续方向（本轮未实现）

- Core 的对象查询参数和 Excel 的读模型都使用运行时属性访问器。若这些功能需要 Native AOT，应先定义调用方如何提供静态映射，再评估泛型接口或 Source Generator，并用 AOT 发布与现有语义测试验证。仅调整动态编译的缓存不能解决这一限制。
- Excel 的 `import-title-map` 阶段应继续用 Profiler 拆分首次 `GetRow` 与本项目列名映射的成本；只有确认后者占主要开销时才改动映射算法。
- 上述微基准证明的是局部路径的分配与耗时变化。是否影响实际服务，仍需代表性请求、音频文件和导出器配置下的端到端测量。
