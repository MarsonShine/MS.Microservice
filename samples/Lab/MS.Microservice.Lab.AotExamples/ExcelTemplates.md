# Excel 模板与颜色：复用声明，不再重新发现

普通导入导出改为显式映射之后，旧模板填充仍会在每个 builder 中扫描属性和特性，再从缓存中取得 `Expression.Compile()` 生成的 getter。两条流程对同一个模型使用不同的发现机制。A03 让 `DynamicExcelBuilder<T>` 接收同一份 `ExcelModelMap<T>`，模板标题只负责决定实际列索引，映射负责决定读取哪个属性。

```csharp
var builder = helper.OpenExcel(templateStream, rows, BookMap);
builder.InitInsertRow(0, 1).InsertCellValue(1);
await builder.WriteAsync(output);
```

模板标题存在时按名称绑定，保留非连续列的位置；没有标题单元格时按映射声明顺序填入。插入行、复制样式、移动尾部行和写入流仍由原有 NPOI 代码完成。getter 只缓存访问方式，不缓存属性值，因此绑定后修改对象仍会读取最新值。`ReflectionDelegateFactory` 已退出生产组件。

颜色表的旧实现会在每次创建 `ExcelColorMap` 时扫描 NPOI 的所有颜色类、构造对象并反射读取 `Index`。颜色名称与索引对指定 NPOI 版本是固定数据，所以新实现直接引用 `HSSFColor.Red.Index` 等常量，保存为所有实例共享的只读字典。使用 `nameof` 让名称与编译时类型对应；大小写规则仍为 ordinal。更换 NPOI 版本时，完整颜色对照测试会发现新增或改变的颜色。

完整旧模板、委托工厂和颜色表保存在 `Legacy/Excel/*.cs.txt`。`Static/Excel/TemplateMapping.cs` 独立展示标题索引绑定；生产实现进一步把绑定结果复用于多行。验证覆盖样式、值类型、空标题、稀疏和重排标题、缺失列、空数据、尾行移动、管道输入以及整个 NPOI 颜色表。

这里移除的是自有代码中的运行时成员发现与委托编译，属于 **AOT compatibility**。不据此宣称整个 NPOI 依赖树可 NativeAOT 发布，也不声称已经缓存的 getter 原先每行都会重新编译。
