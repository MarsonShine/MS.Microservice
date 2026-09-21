# Excel 旧接口与 AOT 实现并存

早先切片将 MiniExcelHelper、ExcelColumnAttribute 和隐式模型发现从生产组件移出。为保留原来的调用方式，后续兼容性修正已恢复这些接口；历史 `.cs.txt` 快照继续供阅读，运行中的旧代码在 `MS.Microservice.Excel/src/MS.Microservice.Excel`。

当前静态实现完整迁到 `MS.Microservice.Excel/src/MS.Microservice.Excel.Aot`，命名空间为 `MS.Microservice.Excel.Aot`。它包含模型映射、常规导入导出、模板、静态颜色表和诊断代码。运行时位于独立的 MS.Microservice.Excel.Aot.csproj，生成器位于独立的 MS.Microservice.Excel.Generator.csproj。详细命令、依赖隔离和调用方法见 [AOT 目录说明](../../../MS.Microservice.Excel/src/MS.Microservice.Excel.Aot/README.md)。

旧 Excel 项目保留注解、旧接口和 MiniExcel 依赖；AOT 项目只包含静态实现，未引入动态兼容回退。测试同时引用两个运行时项目，分别验证两套接口。Core 的通用动态属性访问器仍未恢复，旧 Excel 所需的 getter/setter/工厂由其内部兼容代码提供。

这次测试直接执行完整旧 ExcelHelper 与新 ExcelHelper，而不只比较简化的 ColumnDiscovery 示例。原回归矩阵和新增交叉读写覆盖真实工作簿的列、格式、转换、流、公式和模板。学习区的独立示例仍用于解释机制，不能替代组件测试。
