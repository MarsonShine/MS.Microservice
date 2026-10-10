# Excel 导入校验与错误报告

现有 `Import`／`ImportAsync` 保持兼容：空单元格保留模型默认值，无法转换的文本也可能保留默认值，`ExcelColumn.Required` 不在旧入口强制执行。需要校验时使用同一个 `ExcelHelper` 的 `ImportWithReport`／`ImportWithReportAsync`，复用已有 NPOI、属性访问器缓存和类型转换。

```csharp
var helper = new ExcelHelper().InitSheetIndex(0).InitStartReadRowIndex(0, 1);
try
{
    var report = await helper.ImportWithReportAsync<OrderRow>("orders.xlsx", input,
        row => row.Quantity < 0
            ? [new ExcelRowValidationError("数量不能为负数", nameof(OrderRow.Quantity))]
            : [], cancellationToken);

    foreach (var row in report.Rows)
        Process(row.RowNumber, row.Value);

    if (!report.IsValid)
        new ExcelHelper().Export(report.Errors, "Errors", errorOutput);
}
finally
{
    helper.Workbook?.Dispose();
}
```

`Rows` 只包含成功转换并通过业务校验的行，同时保留原始 Excel 行号。`Errors` 是可直接导出的平面模型，包含工作表、行列位置、属性名、错误类型、消息和原始单元格表示。行列号从 1 开始，初始化的标题／内容索引仍从 0 开始。不存在的物理行跳过；已经存在的空行按普通行校验。未知列忽略，非必填列缺失或为空时保留模型默认值。

`ExcelColumn.Required = true` 要求列存在，且每行对应源值不是缺失、空白或空白字符串；数值 `0`、布尔值 `false` 均有效，属性初始化值不能替代缺失的源值。转换失败、Excel 错误单元格、公式错误都会排除该行；数值显示格式不会将小数舍入成可接受的整数。类型转换沿用已有支持范围，额外领域约束交给业务校验，例如枚举的业务允许值或金额范围。

读取模型沿用现有属性访问器规则：需要公开无参构造函数，列映射到公开、可读的实例属性；字段不参与映射。缺少必填标题、重复映射标题、多个模型属性使用同一列名和映射到不可写属性（含只读或 `init` 属性），作为模板错误定位到标题行，返回错误且不导入数据行。列名比较沿用现有的序号比较，区分大小写。业务校验仅对单元格校验通过的对象调用，返回属性名和消息即可；真实行列由导入器分配，未指定属性的错误定位到整行。一个行可以返回多个错误。回调和用户属性 setter 的非预期异常会直接传播，不伪装成单元格错误。

调用者负责释放 `helper.Workbook`；本组件不会替调用者关闭传入的流。辅助类实例保存当前工作表等状态，应按操作创建实例，避免并发使用。取消会在缓冲复制、行列处理和业务校验枚举阶段检查；NPOI 打开工作簿仍是同步操作。异步入口对不可定位流先做内存缓冲，NPOI 本身也在内存中加载工作簿，因此这不是大文件流式导入接口。导出的错误报告使用现有导出器，也保持目标流打开。

NPOI 2.8 的流构造路径会关闭输入，组件在导入和模板打开的共同读取边界添加了不关闭底层流的只读适配。旧导入入口也因此保留调用者流，模型默认值及转换兼容行为不变；正常读取、异常、取消及工作簿释放后，底层流仍由调用者管理。
