# HTTP 上传校验设计

调用方式、允许类型和返回状态见 [AspNetCore README](../README.md#upload-validation)。实现为 [FileUploadValidator](../Uploads/FileUploadValidator.cs)，Lab 端点复用该组件。

校验顺序是声明长度、扩展名／MIME 配对、文件头。业务失败返回 `FileUploadValidationResult`；取消和读取异常继续抛出。宿主应提供有效 `UploadFileKind` 与正数预算，并配置整个请求体限制；组件不会读取全量正文来核对声明长度。

文件头读取循环累积分段结果，兼容不可定位流；图片至多 12 字节，Excel 至多 8 字节。读取流由校验器通过 `OpenReadStream()` 创建并释放，不能交给下游继续读取。

| 格式 | 签名判断 |
| --- | --- |
| JPEG | 起始 `FF D8 FF` |
| PNG | 8 字节 PNG 签名 |
| WebP | 开头 `RIFF`，偏移 8 为 `WEBP` |
| XLS | 8 字节 OLE 容器签名 |
| XLSX | 起始 ZIP local-file 签名 |

签名只排除明显不匹配，不能证明结构、解码或工作簿内容有效。XLSX 的 ZIP 外观与普通 ZIP 相同，短数据也可能包含完整签名；完整验证应由拥有格式不变量的图片／Excel 处理层执行。

大小、类型策略和错误消息属于 HTTP 入口；随机保存与文件生命周期属于 Core 或宿主适配。Lab 的目录约束、配置和书籍分类导入规则继续留在 Lab，通用组件不反向依赖它们。

维护时应保留边界大小、短读、错误 MIME／扩展名、仅签名内容、取消和流释放的回归。测试入口为 [AspNetCore.Tests](../../../test/MS.Microservice.AspNetCore.Tests) 中的上传测试，Lab 调用链另由 [Lab.Tests](../../../test/MS.Microservice.Lab.Tests) 验证。
