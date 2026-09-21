# Excel：把机械映射交给 Source Generator

最初的 ExcelHelper 在运行时扫描属性并生成访问器。第一版 AOT 实现把这些信息改为调用方手写的 ExcelModelMap；它去掉了动态发现，却要求每个调用方重复写字段名、getter、setter、转换器。内部 Func<T, object?> 也仍会装箱值类型。

现在使用增量 Source Generator 读取编译期类型符号。调用方只声明模型、可选列注解和生成上下文，自动得到静态映射：

```csharp
[ExcelSerializable(typeof(Book))]
internal static partial class Books;

helper.Export(rows, "Books", Books.Book);
helper.Import("books.xlsx", stream, Books.Book);
```

生成器为每列产生直接访问属性的读写操作，例如 `cell.SetCellValue((double)model.Id)` 和 `reader.TryReadNumber<int>(..., out var value)` 后赋给 `model.Id`。不同类型的值不再先装箱成 object，枚举底层转换也在生成代码中以具体类型完成。ExcelModelMap 只保存这些生成操作和列名；表头绑定属于工作簿数据处理，仍在运行时一次完成。

特殊构造使用上下文的静态 Factory；特殊转换使用 IExcelCellConverter<T>。除此之外不要求调用方提供逐字段委托。列名、顺序、忽略属性和只读规则均由声明决定，缺失工厂、重复/空列名或不支持类型在编译时报 EXCEL001。

[运行时目录说明](../../../MS.Microservice.Excel/src/MS.Microservice.Excel.Aot/README.md)给出完整声明、构建和包消费方式。生成器使用独立的 MS.Microservice.Excel.Generator 项目；生成器随 AOT 包作为 analyzer 交付，不成为运行时依赖。手写 getter/setter API 已删除，不保留兼容回退。

## 学习区里的三个阶段

- `Legacy/Excel/ColumnDiscovery.cs`：最初反射发现的机制例子，完整旧 ExcelHelper 历史快照仍保留。
- `Legacy/Excel/ManualMapping/ExcelModelMap.cs`：本次被替换的完整手写映射实现，可编译；同目录的 ColumnMapping、TemplateMapping 是先前阶段的简化例子。
- `Static/Excel/GeneratedBook.cs`：模型与生成上下文。编译时在学习程序集内生成真正的属性访问代码，不是用一个手写例子冒充 SG。

ExcelGeneratedMappingExampleTests 使用相同输入检查旧映射读取值和新映射实际写出的工作簿。生成器专门测试会编译生成代码，也验证单列、多列、多个 partial 声明、继承、闭合泛型、required 工厂、非法声明和同名上下文。实际工作簿矩阵覆盖原有日期、公式、枚举、可空值、只读列、特殊转换、流、模板及颜色规则。

本机预热后，一万次旧 object 数值 getter 分配 240,000 字节；生成列的一万次写入加读取分配 0 字节。测试使用已创建的 HSSF 数值单元格，专门观察列访问，不把结果描述成完整 Excel 文件零分配或整体吞吐量提升。自有运行时与生成代码做裁剪/AOT 静态分析，不执行 NPOI 或应用的 NativeAOT 发布验证。
