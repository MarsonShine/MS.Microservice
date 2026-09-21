# 退出旧 Excel 动态入口

`MiniExcelHelper` 把任意 POCO 交给 MiniExcel 内置映射，成员发现与属性映射由依赖库在运行时执行。自有 NPOI 辅助类已经迁到显式 `ExcelModelMap<T>`，但留下这个入口会保留一条未经静态映射约束的替代路径。这是 **AOT compatibility** 边界清理，没有把 MiniExcel 的整体性能描述成较差。

仓库调用扫描没有发现 MiniExcelHelper 消费者。移除该入口及生产组件的 MiniExcel 依赖，原文件以 `.cs.txt` 保存；不是把第三方库重写一遍。Excel 不再使用 Core 的动态属性访问器，因此也删除无用途的 Core 项目引用。旧 `ExcelColumnAttribute` 已不参与导入/导出；移除属性定义和 Lab 残留注解，防止使用者误以为标注仍能控制列。

原 `helper.Import<Row>(bytes)` 迁为 `helper.Import(bytes, rowMap)`，映射显式提供列标题、读取/写入、转换器和工厂。完整例子见 [ExcelModelMappings.md](ExcelModelMappings.md)，模板见 [ExcelTemplates.md](ExcelTemplates.md)。工厂允许无默认构造函数的模型；没有 setter 的列保持只导出。

`Static/Excel` 已有独立静态映射例子，与 Legacy 使用同一输入对照。组件套件继续验证真实工作簿的列顺序、类型转换、日期/公式、模板和颜色，Lab 测试验证实际导入端点。保留 NPOI 不等于宣称其内部实现已通过 NativeAOT；没有执行发布验证。
