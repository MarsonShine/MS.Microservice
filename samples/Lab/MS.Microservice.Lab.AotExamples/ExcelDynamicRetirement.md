# Excel 旧接口与 AOT 实现并存

早先切片将 MiniExcelHelper、ExcelColumnAttribute 和隐式模型发现从生产组件移出。为保留原来的调用方式，后续兼容性修正已恢复这些接口；历史 `.cs.txt` 快照继续供阅读，运行中的旧代码在 `src/MS.Microservice.Excel`。

当前静态实现完整迁到 `src/MS.Microservice.Excel.Aot`，命名空间为 `MS.Microservice.Excel.Aot`。它包含模型映射、常规导入导出、模板、静态颜色表和诊断代码。没有新建项目；唯一的 Excel.csproj 通过 ExcelVariant 选择编译/打包内容。详细命令、依赖隔离和调用方法见 [AOT 目录说明](../../../src/MS.Microservice.Excel.Aot/README.md)。

Legacy 模式保留注解、旧接口和 MiniExcel 依赖；Aot 模式只包含静态实现，未引入动态兼容回退。默认 All 模式供仓库同时编译和测试两套实现，不允许直接打包。Core 的通用动态属性访问器仍未恢复，旧 Excel 所需的 getter/setter/工厂由其内部兼容代码提供。

这次测试直接执行完整旧 ExcelHelper 与新 ExcelHelper，而不只比较简化的 ColumnDiscovery 示例。原回归矩阵和新增交叉读写覆盖真实工作簿的列、格式、转换、流、公式和模板。学习区的独立示例仍用于解释机制，不能替代组件测试。
