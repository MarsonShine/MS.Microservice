# MS.Microservice.Excel.Aot

这里保存 Excel 的显式模型映射实现，所有公开类型位于 `MS.Microservice.Excel.Aot` 命名空间。目录包含 ExcelHelper、导入导出接口、ExcelModelMap、模板填充、静态颜色表和内部诊断代码；没有独立 csproj。

现有 `../MS.Microservice.Excel/MS.Microservice.Excel.csproj` 通过 Compile Include 编译这里的源码。原目录同时保留原命名空间的旧接口，包括属性注解、无 map 的 Import/Export、模板入口和 MiniExcelHelper。仓库的 Lab 导入服务继续显式使用 AOT 命名空间。两套实现不互相调用，也不引用学习类库。

## 一个项目，分别打包

在仓库根目录执行，版本由发布者指定：

```powershell
dotnet pack src/MS.Microservice.Excel/MS.Microservice.Excel.csproj -c Release -p:ExcelVariant=Legacy -p:PackageVersion=1.0.0-local.1 -o artifacts/excel-packages
dotnet pack src/MS.Microservice.Excel/MS.Microservice.Excel.csproj -c Release -p:ExcelVariant=Aot -p:PackageVersion=1.0.0-local.1 -o artifacts/excel-packages
```

| ExcelVariant | 编译内容 | 程序集及包 |
|---|---|---|
| All（默认） | 旧接口 + 新 AOT 命名空间，供仓库开发与对照测试 | 不允许打包，必须明确选下面一种 |
| Legacy | 原目录的兼容实现，依赖 MiniExcel 和 NPOI | MS.Microservice.Excel |
| Aot | 仅本目录的静态映射实现，不包含旧类型或 MiniExcel | MS.Microservice.Excel.Aot |

每个模式使用各自的 `obj/<variant>` 和 `bin/<variant>`，包括独立的 NuGet assets、生成代码和 Release 输出。这样先构建旧版再打 AOT 包时，不会复用旧依赖图或旧 DLL。使用 `--no-restore`、`--no-build` 时，必须先为同一个 ExcelVariant 完成对应步骤。普通 dotnet pack 未选择模式会明确失败，避免把开发时的组合程序集当成独立包。

仓库 `pack-modules.ps1 -Modules MS.Microservice.Excel` 继续产出旧包；AOT 包使用上面的同项目命令。源码导出脚本导出 Excel 时会同时携带本目录，保证 Compile Include 的路径完整。没有新增项目或修改解决方案项目列表。

## 调用对照

```csharp
// 旧调用方保持原命名空间和签名。
var old = new MS.Microservice.Infrastructure.Utils.ExcelHelper();
var oldBytes = old.Export(rows, "Rows");

// 新调用方明确使用 AOT 命名空间与模型映射。
var current = new MS.Microservice.Excel.Aot.ExcelHelper();
var newBytes = current.Export(rows, "Rows", rowMap);
```

rowMap 的完整声明见[模型映射说明](../../samples/Lab/MS.Microservice.Lab.AotExamples/ExcelModelMappings.md)。旧 `[ExcelColumn]` 不控制 AOT 版；新版本只执行显式声明的列和委托。

旧实现从改造前源码恢复，唯独原 Core.PropertyAccessors 依赖改为 Excel 内部的 ExcelPropertyAccessors。它使用旧 ReflectionDelegateFactory 创建普通 getter/setter/工厂，保留公开实例属性、排除索引器、跳过私有/init setter 和公开无参构造函数要求；原 Excel 元数据缓存仍负责复用。这样无需把动态查询访问器重新放回 Core。旧版继续需要反射和运行时编译，不属于 AOT 兼容实现。

## 验证

Excel 原接口的完整回归测试位于 `test/MS.Microservice.Excel.Tests/Legacy`；现有映射、模板、颜色和诊断测试改为调用 AOT 命名空间。`ExcelCompatibilityTests` 直接比较两套实际实现，互相读取生成的工作簿，检查空集合、空值、中文、列顺序、忽略列和 MiniExcel 往返。`.cs.txt` 历史快照不参与这些测试。

`build/validate-static-aot.ps1` 对 Excel 选择 Aot 模式，实际检查纯 AOT 源码的裁剪/动态代码诊断；不会把有意保留的 Legacy 实现伪装成无反射实现。NPOI 的完整 NativeAOT 发布兼容性仍未验证。这里的本地 NuGet 打包不等于发布包到远程源，也不等于 NativeAOT publish。

本次本地验证还检查两个 nupkg 的程序集类型和依赖清单：旧包只含原命名空间，AOT 包只含新命名空间，后者没有 MiniExcel。兼容测试另外覆盖只读属性、私有 setter、init setter、私有 getter 和索引器，确认 Excel 内部访问器没有扩大旧接口的写入范围。

本次回归：Excel 67 项、Lab 284 项、边界检查 39 项通过；19 项原有性能基准跳过。源码导出后的构建通过；Aot 变体实际加载 ILLink 10.0.11 后零诊断。
