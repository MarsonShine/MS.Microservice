# 重试延迟边界
基线3df005f，范围见 [契约](task.md)。

根因：旧指数策略先将计算值转换TimeSpan再封顶，巨大指数或TimeSpan.MaxValue边界先溢出；零base乘Infinity变NaN。共享执行器直接Task.Delay不支持超出单timer范围的合法正TimeSpan。F旧int倍增也可能回绕为负数。

修复：有限multiplier在转换前封顶，零base保持0；合法超长等待分段并保持总延迟/取消，不加入业务封顶。F复用上一片TimeSpan计算；负delayMillis现在明确抛参数异常，这是非法输入行为的有意修正，正常0/正参数和旧默认值保留。

## 验收
- 修复前 `dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~RetryDelayBoundaryTests --logger "console;verbosity=normal"`：13项中10失败、3通过，见 [红灯输出](red-tests.txt)。
- 修复后 `dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet`：85 passed / 0 failed / 0 skipped，见 [输出](tests.txt)。
- 覆盖巨大指数、零base、默认/显式封顶、TimeSpan最大值、0/.5倍数、超timer范围等待取消、负F延迟提前拒绝；同时复跑前两片取消/堆栈/同步上下文/兼容测试。
- 子代理只读复查未发现明确缺陷。长等待只验证合法进入等待和取消，不实际等待数十天。

## 三片交付
1. [执行契约](../CORE-RETRY-EXECUTION/summary.md)：2a222a8，新增取消入口、保留异常堆栈、消除同步上下文死锁。
2. [函数式入口](../CORE-FUNCTIONAL-RETRY/summary.md)：3df005f，F.Retry复用同一循环，不引入五分钟默认封顶。
3. 本片：延迟边界修复及最后85项回归。

未引入成熟库适配层或新项目，不改HTTP/AI/Networking。不推送。上述测试是Core内相关行为验证，未运行全仓/真实服务/性能或漏洞审计。
