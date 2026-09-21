# MS.Microservice.Excel

打开 [MS.Microservice.Excel.slnx](MS.Microservice.Excel.slnx)，集中开发 Excel 的三个生产项目与一个测试项目。根解决方案仍包含这些项目，供全仓库验证。

```text
MS.Microservice.Excel/
  MS.Microservice.Excel.slnx
  src/
    MS.Microservice.Excel/           # 原 NPOI / MiniExcel 实现
    MS.Microservice.Excel.Aot/       # 静态类型化运行时与独立包
    MS.Microservice.Excel.Generator/ # 编译期 Source Generator
  test/
    MS.Microservice.Excel.Tests/    # 工作簿、生成器与兼容性测试
```

| 阅读目的 | 入口 |
|---|---|
| 使用生成式导入、导出、模板 | [AOT 使用说明](src/MS.Microservice.Excel.Aot/README.md) |
| 理解 SG 配置、发布台账、F12 和生成结果 | [生成器学习说明](src/MS.Microservice.Excel.Generator/README.md) |
| 比较旧机制、手写映射与生成式实现 | [学习区](../samples/Lab/MS.Microservice.Lab.AotExamples/ExcelModelMappings.md) |

在本目录执行：

```powershell
dotnet restore MS.Microservice.Excel.slnx
dotnet test MS.Microservice.Excel.slnx -c Release --no-restore
dotnet pack src/MS.Microservice.Excel.Aot/MS.Microservice.Excel.Aot.csproj -c Release -o ../artifacts/excel-packages
```

原 Excel 包改用 `src/MS.Microservice.Excel/MS.Microservice.Excel.csproj` 打包。生成器不单独发包，它的 DLL 随 AOT 包放入 `analyzers/dotnet/cs`，在调用方编译期间加载。

独立解决方案仍沿用仓库根的 SDK、集中包版本、构建属性和 NuGet 配置，不复制这些设置。测试链接仓库学习区的手写映射对照源码，因此独立打开解决方案不等于可以只复制这个目录到仓库外；生产模块源码导出见 [组件消费说明](../docs/components/consumption.md)。

性能基线测试默认跳过，需显式设置 `MS_MICROSERVICE_RUN_PERF_TESTS=1` 才执行。普通测试通过不等于第三方依赖已经通过 NativeAOT 发布验证。
