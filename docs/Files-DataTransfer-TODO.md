# Files 与 DataTransfer

目标是复用现有组件，跑通常见的文件保存、下载、删除和 Excel／CSV 导入导出流程。先满足单次调用的使用场景，新增代码以现有接口的实际缺口为依据。

## 已有能力

| 流程 | 现有实现 | 后续检查点 |
| --- | --- | --- |
| 文件写入、读取、元数据、删除 | [IFileStorage](../src/MS.Microservice.Core/IO/IFileStorage.cs)、LocalFileStorage、[S3FileStorage](../src/MS.Microservice.Storage.S3/README.md) | 保留统一存储契约，检查调用方还需重复哪些衔接代码 |
| HTTP 上传、下载、删除 | [FileToolsController](../samples/Lab/MS.Microservice.Lab/Controller/FileToolsController.cs) | 已复用现有大小限制和用户隔离，本地下载直接输出文件流，上传返回原始叶子文件名 |
| Excel 导入、校验、错误报告与导出 | [Excel](../src/MS.Microservice.Excel/README.md)、SpreadsheetToolsController | 已补充整份校验成功后计算商品汇总的示例，复用行列定位和错误文件导出 |
| CSV 读写 | CsvHelper、ExcelHelper.Csv、SpreadsheetToolsController | 已复用 Excel 模型映射、校验和报告，跑通商品导入导出及业务汇总 |
| 现有 HTTP 验证 | test/MS.Microservice.Lab.Tests/Tools 中的文件及表格测试 | 复用已有测试，新增检查只覆盖此次改变的行为与缺口 |

Files 与 DataTransfer 是本轮能力范围名称，是否需要新增项目由实际代码决定。文件信息作为调用结果返回，业务需要保存时自行处理；不引入元数据数据库。输入大小限制、流释放和调用方授权沿用现有契约。

## Slice 清单

| Slice | 状态 | 建议 commit 标题 | 验收 | 依赖 |
| --- | --- | --- | --- | --- |
| 1：现状与待办 | 完成 | docs: record Files and DataTransfer scope and backlog | 入口存在，范围与已确认需求一致 | 无 |
| 2：Files 常用流程 | 完成 | feat(files): complete common file flows using existing storage | 保存后可查询、下载和删除；现有用户隔离仍有效；验证受影响的本地及 S3 契约 | 1 |
| 3：DataTransfer 常用流程 | 完成 | feat(data-transfer): connect common import and export flows | Excel／CSV 导出后可导入；错误有位置；示例展示校验后业务处理；流正确释放 | 1；若使用 Files 保存产物则依赖 2 |

每片将实现、必要测试和使用说明放在同一提交内。后续片开始时再根据代码明确改动文件与验证范围。

## 当前交付待办

- [x] 梳理已有文件存储与表格接口，确定复用入口。
- [x] Files：补齐实际缺失的调用衔接，跑通保存、查询、下载、删除。
- [x] Files：更新[最短接入示例](components/files.md)，验证受影响的存储及 HTTP 流程。
- [x] DataTransfer：跑通 Excel／CSV 导入、校验、业务处理与导出示例。
- [x] DataTransfer：复用错误报告，补充此次改动所需的验证和[使用说明](components/data-transfer.md)。

## 遇到实际需求后再评估

以下仅记录候选需求，不属于当前承诺范围，也不提前建立接口或配置。

- 文件元数据持久化、业务绑定、临时文件过期清理：出现业务附件生命周期管理需求时评估。
- 分片上传、断点续传：普通上传无法满足实际文件规模或网络条件时评估。
- 文件版本、去重：出现明确的版本保留或重复存储问题时评估。
- 后台导入导出、进度和执行历史：单次请求无法满足实际耗时或规模时接入现有 Tasks。
- 导入预览确认、分批提交、失败恢复：出现明确业务交互或事务需求时评估。
- Realtime、Webhooks、Content、FeatureManagement：暂缓，另有具体需求时再规划。
