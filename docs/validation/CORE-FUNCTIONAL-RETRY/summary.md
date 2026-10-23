# 函数式重试共用执行器
基线2a222a8。范围见 [契约](task.md)。F.Retry原签名保留，复用RetryExecutor及指数策略；显式maxDelay=TimeSpan.MaxValue，不引入五分钟默认封顶，不再递归或使用int乘二计算。

验收：dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet。
72 passed / 0 failed / 0 skipped，见 [输出](tests.txt)。新增10项验证非正次数、额外尝试次数、同步/异步异常、独立取消、原始堆栈、空结果及零延迟连续重试。延期计算极值由第三片处理。

不新增公开策略或Builder；HTTP、AI、Networking未改动。不推送。
