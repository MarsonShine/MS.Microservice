# Excel 模型映射：把运行时发现移到调用方声明

旧实现先扫描属性和 `ExcelColumnAttribute`，再从 `PropertyAccessors` 获取动态生成的 getter、setter 和构造器；结果按类型缓存。缓存已经避免逐行反射，因此这次主要解决 **AOT compatibility**，没有声称每一行都会省掉一次反射。完整旧实现保存在 `Legacy/Excel/ExcelHelper.cs.txt`，可运行的最小机制在 `Legacy/Excel/ColumnDiscovery.cs`。

新实现要求调用方提供 `ExcelModelMap<T>`：工厂决定如何创建每一行对象，列数组决定顺序与列名，普通泛型委托负责读写，转换器负责文本解析。编译器能看到具体构造器和属性访问，不需要运行时寻找成员或生成 IL。`Static/Excel/ColumnMapping.cs` 独立展示了声明列与执行委托的核心过程。

```csharp
static readonly ExcelModelMap<Book> BookMap = new(
    static () => new Book(),
    ExcelColumn<Book>.Create("编号", static b => b.Id,
        static (b, id) => b.Id = id, ExcelValueConverters.Int32),
    ExcelColumn<Book>.Create("名称", static b => b.Name,
        static (b, name) => b.Name = name, ExcelValueConverters.String));

helper.Export(books, "Books", BookMap);
helper.Import("books.xlsx", stream, BookMap);
```

映射适合存为 `static readonly` 并复用；它缓存的是访问方式，读取的仍是对象当前值。旧特性的 `Order` 改为数组顺序，`Ignore` 改为不声明该列，只读列传 `null` setter。映射支持类模型；按值传递的结构体无法通过 `Action<T, TValue>` 写回，因此接口直接限制为 `class`。带参数构造器可以由工厂显式调用，已不要求公共无参构造器。

转换行为仍在原来的 Excel 层完成：数字单元格直接读取，整数不截断小数；日期保留 Excel 日期序列转换；公式先求值；空白或解析失败保留工厂默认值。枚举用 `ExcelValueConverters.Enum<TEnum>()`，可空值用 `Nullable(...)`，自定义类型显式提供 `ExcelValueConverter<TValue>`，不会退回反射转换。DataTable 导出不需要模型映射。

验证包括旧有工作簿与流测试，以及显式顺序、未声明的异常 getter、只读列、带参工厂、公式、日期、可空数值、Guid、枚举、自定义转换和无效整数。这里只验证自有映射层；NPOI 和整个应用的 NativeAOT 发布兼容性不在这次验证结论中。模板填充仍在后续 A03 迁移。
