# 工作簿的行号、容量和模板样式

## 只有三条数据，为什么曾经预留八万个槽位？

普通导入 `Import<T>` 打开工作簿并映射标题，再由 `ReadBody` 创建结果列表。设标题在下标 0，数据在 1、40000、80000，其余行没有记录。这里下标从 0 起，报告展示给用户的 Excel 行号则从 1 起。

```diff
-var list = new List<T>(Math.Max(sheet!.LastRowNum - contentRowIndex + 1, 0));
+var targetSheet = sheet!;
+var logicalRows = Math.Max(targetSheet.LastRowNum - contentRowIndex + 1, 0);
+var list = new List<T>(Math.Min(logicalRows, targetSheet.PhysicalNumberOfRows));
```

从下标 1 开始读，旧公式得到 80000。对于引用类型模型和 x64 运行时，仅列表后备数组的数据区就要 `80000 × 8 = 640,000` 字节。这个工作簿只保存了 4 个物理行（含标题），新公式预留 4 个引用，最终返回 3 个对象。连续工作表上，这个上限仍可容纳标题之后的全部数据。物理存在的空行也要创建默认对象，不能只数有值的行。[容量计算](../ExcelHelper.cs#L247) · [实际空行与容量回归](../../../test/MS.Microservice.Excel.Tests/Excel/ExcelHelperEdgeTests.cs#L51)

## 扫到一个空位后，可以直接跳到下一条数据吗？

旧版在 `GetRow(i)` 返回 null 后直接 `continue`。上例从 1 到 80000，行处理循环会查满 80000 个下标。新版保留逐行读取作为正常路径，并记录连续没有行记录的次数。次数达到物理行数时，查找当前表中大于当前位置的最小物理行号，下一轮从那里继续。[循环与门槛](../ExcelHelper.cs#L269) · [查找后继](../ExcelHelper.cs#L324)

![稀疏工作表中，按逻辑下标遍历和寻找下一物理行的步骤对照](assets/sparse-row-scan.svg)

例子中的第一个长空洞只探测 2、3、4、5，然后遍历 4 个真实行找到 40000。第二个长空洞同理。把行体读取、空位探测和两次物理扫描的索引校验都算上，`ReadRows` 及其跳行方法共调用 `GetRow` **3 + 8 + 2 × 4 = 19 次**。这个算式不包含 NPOI 打开文件、读取标题及业务回调中的工作。

门槛来自一次枚举需要访问的物理行数。密集数据中一个单独的缺行只做一次空位探测，避免反复枚举整张表。NPOI 2.8.0 的 HSSF 使用字典保存物理行，因此代码取最小后继，保持原先的升序。枚举在进入模型 setter 或校验回调前完成，之后不留待访问行的快照。回调增加或删除后续行时，下一轮仍能看到变化。若回调直接修改 `IRow.RowNum`，可能出现行自己报告的编号与工作表索引不一致，此时继续逐下标读取。[索引一致性](../ExcelHelper.cs#L332) · [XLS 与 XLSX 回调改行测试](../../../test/MS.Microservice.Excel.Tests/ExcelImportReportTests.cs#L395)

## 模板插入 128 行，标题样式需要读取几遍？

`DynamicExcelBuilder<T>.InitInsertRow` 先映射标题并移动模板的后续行，再为每个待插入行创建单元格。原来的内层循环反复从同一份标题单元格取样式。

```diff
-foreach (ICell cell in titleRow.Cells)
+foreach (var (columnIndex, style) in templateStyles)
 {
-    ICell contentCell = contentRow.CreateCell(cell.ColumnIndex);
-    contentCell.CellStyle = cell.CellStyle;
+    ICell contentCell = contentRow.CreateCell(columnIndex);
+    contentCell.CellStyle = style;
 }
```

NPOI 2.8.0 的 XSSF 路径从样式表获取 `XSSFCellStyle` 包装对象，HSSF 也会在读取单元格样式时构造包装。若标题有 8 个实际单元格，插入 128 行，旧循环从标题读取样式 `128 × 8 = 1024` 次。新版在本次插入中先把 8 个实际列下标及样式取出，供每行使用，只读 8 次。目标单元格仍创建并赋值 1024 次。快照使用各标题单元格真实的 `ColumnIndex`，因此能保留中间跳列，也能复制没有绑定模型属性的模板列。[样式快照与写入](../DynamicExcelBuilder.cs#L32)

样式属于各自工作簿，每次调用重新取样。测试先复制下标 0 和 3 的两种样式，再修改标题样式并执行第二次插入，检查新行使用修改后的样式，而之前插入并下移的行仍保留原样式。[跨两次插入的回归](../../../test/MS.Microservice.Excel.Tests/Excel/DynamicExcelBuilderTests.cs#L70)

Release 基准分别测了密集和稀疏工作簿的完整读取，以及单行与 128 行模板的构建。模板场景计入创建工作簿、标题和目标行，测试终点为 `InitInsertRow` 返回；生成 ZIP 文件的耗时另属写盘阶段。见[基准输入](../../../benchmarks/MS.Microservice.Excel.Benchmarks/WorkbookCases.cs)和[实测数据与条件](../../../docs/validation/EVENTSOURCING-EXCEL-PERF/summary.md)。
