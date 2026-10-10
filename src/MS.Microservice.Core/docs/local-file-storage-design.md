# 本地文件存储设计

使用示例、参数和资源所有权见 [Core README](../README.md#文件存储)。实现位于 [LocalFileStorage](../IO/LocalFileStorage.cs)，共享契约为 [IFileStorage](../IO/IFileStorage.cs)。

`StoredFileKey` 将名称限制为 32 个小写十六进制字符、一个点和 1–9 个小写 ASCII 字母／数字。扩展名仅控制命名，不证明文件类型。HTTP 类型与文件头检查属于 AspNetCore 上传边界，文件记录和授权属于宿主。

写入先创建同目录、独占的新暂存文件，复制并关闭后通过不覆盖目标的移动发布。暂存文件在成功创建后才归当前操作清理；并发写同 key 时，只有一次移动能成功。失败清理不触碰已存在的目标。原始读写、取消或发布错误会保留；清理同时发生 I/O／权限错误时，二者组成 `AggregateException`。

构造时固定目录的绝对路径，但不建立文件系统隔离。宿主必须控制目录及链接变化；断电或强制终止后的暂存保留期由宿主处理。完成发布后的取消不会回滚结果。

元数据是查询时的长度和最后修改时间快照。读取使用异步文件流，允许共享删除，因此删除 key 后，既有读取句柄仍可读完原内容。删除缺失 key 幂等。该行为不等于应用文件记录与物理文件之间有事务。

`IFileStorage` 也用于可选对象存储适配器。远端请求失败可能无法判断服务端是否完成，调用者预先持有 key 后查询协调；契约只描述当前对象，不负责版本桶历史。Lab 的上传适配保留目录约束和业务配置，通用组件不依赖 Lab。

回归范围见 [本地存储测试](../../../test/MS.Microservice.Core.Tests/IO/LocalFileStorageTests.cs)和[生命周期测试](../../../test/MS.Microservice.Core.Tests/IO/FileStorageLifecycleTests.cs)：当前位置、不可定位输入、并发新建、删除后读取、取消，以及原错误与暂存清理错误并存。
