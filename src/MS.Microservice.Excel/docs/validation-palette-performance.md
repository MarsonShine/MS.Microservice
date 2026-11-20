# 哪些数据能跨行或跨实例复用？

## 连续校验 256 行，只需要几份错误列表？

`ImportCsvWithDataAnnotationsAsync` 和工作簿入口都逐行等待 .NET 的异步数据注解校验。旧实现每进入一行就创建 `List<ValidationResult>`。即使 256 行全部有效，仍会创建 256 个列表，随后构造用于展开成员名的 LINQ 序列。[工作簿校验循环](../ExcelHelper.DataAnnotations.cs) · [CSV 校验循环](../ExcelHelper.Csv.cs)

```diff
-var results = new List<ValidationResult>();
+results.Clear();
 await Validator.TryValidateObjectAsync(value!, context, results, validateAllProperties: true, cancellationToken)
```

新版每次导入有自己的一份暂存列表。第一行如果有两个错误，会在校验结束后将它们转换为最终报告记录。轮到第二行时，清空暂存列表即可；之前已经发布到报告里的错误仍保留。校验上下文仍然按行创建，避免把上一行的对象、成员或服务上下文带入下一行。两个同时进行的导入也各用各的列表。

再看一条失败的校验。旧版先把每个成员转成临时的 `ExcelRowValidationError`，随后又转成 `ExcelImportError`。新版直接通过 `CreateBusinessError` 生成最终记录，自定义业务回调返回的错误也走这个定位方法。对于没有成员名的错误，`DefaultIfEmpty(null)` 确保仍产生一条行级记录。对于某个属性，取模型中首个同名槽位，再用 `Array.IndexOf(map, i, 0, count)` 定位第一个匹配列；`count` 限定在实际表头范围内，避免读到租借数组的空余部分。[错误定位](../ExcelHelper.cs) · [成员、列号与错误次序回归](../../../test/MS.Microservice.Excel.Tests/AsyncDataAnnotationsImportTests.cs)

## 为什么能共享颜色定义，却要复制字典？

`ExcelColorMap` 通过 NPOI 的 `HSSFColor` 找出所有内置颜色。在本项目使用的 NPOI 2.8.0 中，48 个子类型的 `Index` 都是静态字段。旧版每次创建颜色表，都要查找子类型、创建颜色实例，再反射读取相同的索引。

```diff
-var obj = Activator.CreateInstance(t);
-var value = (short)t!.GetField("Index")!.GetValue(obj)!;
+private static readonly Dictionary<string, short> DefaultColors = typeof(HSSFColor).GetNestedTypes()
+    .Where(typeof(HSSFColor).IsAssignableFrom)
+    .ToDictionary(type => type.Name, type => (short)type.GetField("Index")!.GetValue(null)!);
```

新版在类型初始化时建立默认表，每个公开实例只复制字典。这里的复制保护了真实的调用契约：若 A 执行 `Colors.Clear()`，已存在的 B 和稍后创建的 C 都应该能查到 `Black`，且小写 `black` 仍查不到。直接把同一个可变字典公开给三个实例，就会让 A 改掉 B、C 的结果。基准预热后的数字适用于重复构造，首次仍有一次建表成本。[默认表与公开字典](../ExcelColorMap.cs) · [实例隔离测试](../../../test/MS.Microservice.Excel.Tests/Excel/ExcelColorMapTests.cs)

## 本轮源文件检查范围

Excel 的 13 个生产 C# 文件包括 CSV 状态机和选项、模型 CSV 映射、工作簿读写、异步校验、模板写入、反射访问器、颜色表、列特性、报告类型、导入导出接口、MiniExcel 适配和诊断探针。读取端的 `CsvRow.Fields` 可由调用方保存；类型元数据和属性访问器已有缓存；MiniExcel 使用自身的模型映射；诊断探针只在显式开启的构建中参与阶段记录。修改聚焦能通过公开入口验证的重复开销。

完整的输入规模、耗时、分配与对照边界见[验收记录](../../../docs/validation/EVENTSOURCING-EXCEL-PERF/summary.md)。
