# Slice 09：任务终态在线收敛

Status: complete

范围：Tasks JobExecutor、JobExecutionOptions与注册校验；Tasks.Tests/JobDatabase测试夹具；Tasks执行说明/README；状态/证据。范围外：表结构、IJobStore签名、分布式执行、处理器自动重跑、无限增长补偿队列。

根因：handler/scope结束后终态写入一次失败即移除_active并释放capacity，Running只能等启动恢复。不变量：同一宿主继续核对并重试同一AttemptToken的固定终态操作，不重复handler、不伪造成功、不覆盖新attempt。

方案：保留原Task和queue slot直到提交、原token已Ended或停止；每次fresh scope/既有StorageOperationTimeout，有限且封顶的退避，运行中不永久放弃。异常/false后核对原token，Ended仅表示已结束/失去所有权。停机取消重试等待，已开始的存储调用按自身超时结束；handler因停机结束时仍允许第一次Recover。

新选项：InitialFinalizationRetryDelay默认1秒，MaximumFinalizationRetryDelay默认30秒；沿用Tasks的有限等待配置风格，25毫秒<=initial<=maximum<=5分钟。只增加属性，不更改IJobStore或JobState。

验收：`dotnet test MS.Microservice.Tasks/test/MS.Microservice.Tasks.Tests/MS.Microservice.Tasks.Tests.csproj --no-restore -c Release`；`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.Tasks.EFCore') -ReportPath artifacts/validation/api-compatibility-slice09.json`。SQLite触发器与commit interceptor验证持续失败/恢复、容量、handler只一次、回包丢失、旧token与Stop；既有启动恢复/取消优先继续有效。

结果：Tasks.Tests源码重建20/20通过，0失败0跳过；Tasks生产项目构建0警告0错误。Tasks与Tasks.EFCore两个包API基线比较通过，报告artifacts/validation/api-compatibility-slice09.json，退出码0。git diff --check通过。

真实红测：SQLite触发器阻止终态更新，处理器结束后解除故障；旧实现同一宿主5秒后仍为Running（预期Succeeded），1失败。新实现覆盖终态成功/失败、失败期间保持容量、commit已成功但回包异常、旧token失去所有权、Stop不切换已选终态操作以及重启恢复。处理器与attempt均不重复。

复核：业务取消/超时资源在持久化重试之前释放；每次独立存储scope且保持StorageOperationTimeout；false/异常后查原token，Ended仅停止旧执行。停止取消退避等待，允许已开始写入在自身超时内完成；原handler停机路径仍有首次Recover。无schema、IJobStore或JobState变更。

边界：依赖存储遵守CancellationToken；存储持续故障时占用对应执行容量，直到恢复或停机，持久化未决状态由原启动恢复处理。本片未改变Slice06四入口消费者的受测输入，复用其证据。
