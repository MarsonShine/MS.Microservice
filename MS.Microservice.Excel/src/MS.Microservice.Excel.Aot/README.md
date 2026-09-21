# MS.Microservice.Excel.Aot

本目录提供由 Source Generator 生成模型访问代码的 Excel 导入、导出与模板填充。调用方声明模型和生成上下文，不需要逐字段编写 getter、setter 或类型映射。生成列直接使用属性的具体类型，内置读写路径不经过 object 值委托。

## 声明和使用

```csharp
using MS.Microservice.Excel.Aot;

public sealed class Book
{
    [ExcelColumn("编号")]
    public int Id { get; set; }
    [ExcelColumn("名称")]
    public string? Name { get; set; }
}

[ExcelSerializable(typeof(Book))]
internal static partial class WorkbookModels;

// 表头默认使用属性名；只在改名、排序、忽略或特殊转换时添加 ExcelColumn。
var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
var bytes = helper.Export(books, "Books", WorkbookModels.Book);
var rows = helper.Import(bytes, WorkbookModels.Book);
var template = helper.OpenExcel(templateStream, books, WorkbookModels.Book);
```

一个上下文可以添加多个 ExcelSerializable。Name 可指定生成的属性名，避免两个模型同名。模型无需 partial；上下文是非泛型 static partial class，若嵌套在其他类中，包含类也必须为非泛型 partial class。生成器支持闭合泛型模型。

只读取公开实例属性；静态属性、索引器和非公开 getter 不参与。只读、init 和非公开 setter 的属性只导出。Order 控制列顺序；同 Order 按属性发现的声明顺序排列，继承成员接在派生类成员之后。Ignore 明确排除列。空列名、重复列名、不支持的类型、无效上下文和缺失工厂产生 EXCEL001 编译错误，不回退到运行时成员发现。

自动支持 string、bool、byte/sbyte、short/ushort、int/uint、long/ulong、float/double、decimal、DateTime、Guid、枚举及对应可空值类型。空白或无法解析的输入保留工厂默认值；整数不截断小数。公式先求值；日期公式保留工作簿格式解释。枚举按名称导出、支持名称及底层整数导入，Flags 的组合和未命名数值同样支持。

## 只有特殊行为才需要扩展

没有可访问无参构造函数，或有 required 属性的模型，在上下文声明一个静态无参工厂：

```csharp
[ExcelSerializable(typeof(SeededRow), Factory = "CreateRow")]
internal static partial class WorkbookModels
{
    private static SeededRow CreateRow() => new("initial value");
}
```

特殊列通过 `[ExcelColumn(Converter = typeof(MyConverter))]` 指定实现 `IExcelCellConverter<T>` 的类型。接口只有静态 TryRead 和 Write，T 必须对应属性类型；普通属性不需要转换器。转换器可以使用 ExcelCellReader 的类型化读取、GetText 或 GetFormattedText。没有转换器注册表、自动类型发现或 object 转换回退。

## 独立项目与打包

本项目只编译 AOT 运行时代码。原 Excel 实现仍在同级 `MS.Microservice.Excel` 项目；生成器在同级 `MS.Microservice.Excel.Generator` 项目，以 netstandard2.0 编译并仅在编译期执行。三个项目分别拥有自己的 obj/bin，不再使用 ExcelVariant 或递归构建同一个 csproj。

NuGet 调用方引用 MS.Microservice.Excel.Aot 后，通过包内 `analyzers/dotnet/cs` 自动加载生成器。Roslyn 不成为运行时依赖。仓库内源码调用方还需导入本目录的 ExcelGenerator.props；Excel 测试直接使用生成器 ProjectReference，以便同时执行 GeneratorDriver 测试并导航源码。

```powershell
dotnet pack MS.Microservice.Excel/src/MS.Microservice.Excel.Aot/MS.Microservice.Excel.Aot.csproj -c Release -p:PackageVersion=1.0.0-local.1 -o artifacts/excel-packages
```

原包使用 `MS.Microservice.Excel/src/MS.Microservice.Excel/MS.Microservice.Excel.csproj` 单独打包。通用模块脚本跳过不可打包的 Generator 项目；源码导出会沿项目引用携带生成器项目。

关于 F12、编译过程、每个项目配置节点、ExcelGenerator.props 和 AnalyzerReleases 文件的解释，见 [生成器学习说明](../MS.Microservice.Excel.Generator/README.md)。可用 `dotnet build <调用方项目> -t:Rebuild -p:EmitCompilerGeneratedFiles=true` 查看 obj 下实际生成的 `.Excel.g.cs`。

## 迁移与验证边界

手写 `ExcelColumn<T>.Create(getter, setter, converter)`、ExcelValueConverter 和 ExcelValueConverters 已从 AOT 运行时删除，没有兼容重载。调用点改传生成上下文的模型属性。底层 ExcelModelMap 是生成代码所用的固定列元数据，保留表头到实际列号的绑定；它不负责运行时发现模型成员。DataTable 的数据表示本来就是 object，本次保留其既有导出逻辑。

验证包括真实工作簿、生成代码编译和诊断、声明继承/可空/只读/工厂/转换器、模板样式与列定位，以及本地包自动加载 analyzer 的消费测试。数值列分配测试预创建 HSSF 单元格以隔离工作簿和压缩开销；结果不能推广成整个 NPOI 流程零分配。NPOI 的完整 NativeAOT 发布兼容性仍未验证，本次不执行 NativeAOT publish。
