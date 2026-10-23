# 异步名额租约与 AI 接入
基线：22bdd68。范围见 [任务契约](task.md)。第二片独立本地提交，不推送。

实现：Threading 为调用方持有的 SemaphoreSlim 提供 AcquireAsync，成功取得一个名额后返回 IDisposable 租约，Interlocked.Exchange 保证重复或并发释放只归还一次。等待取消不归还未取得的名额。信号量与资源的生命周期仍由原模块管理。

AI 三个请求路径复用租约，接口、gate 实例和并发配置保持不变。根因是原手工 Release 位于初始化之后或日志/响应清理之后，异常可能跳过释放；using 现在覆盖完整作用域，stream 在枚举完成/释放后才归还名额。其他异常分类及重试逻辑不变。

## 验收
- 旧生产实现上的新增 AI 回归：4 failed / 1 passed，见 [红灯输出](red-ai.txt)。三条路径的诊断初始化异常和响应 Dispose 异常均导致后续请求被泄漏名额阻塞。
- `dotnet test test/MS.Microservice.Threading.Tests -c Release --no-restore --verbosity quiet`：20 passed / 0 failed / 0 skipped，见 [输出](threading-tests.txt)。
- 下列项目均使用 `dotnet test <project> -c Release -p:RestoreSources="$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false --verbosity quiet`：
  - MS.Microservice.AI/test/MS.Microservice.AI.Core.Tests：74 passed，见 [输出](ai-core-tests.txt)。
  - MS.Microservice.AI/test/MS.Microservice.AI.OpenAI.Tests：31 passed，见 [输出](openai-tests.txt)。
  - MS.Microservice.AI/test/MS.Microservice.AI.DeepSeek.Tests：10 passed，见 [输出](deepseek-tests.txt)。
  - MS.Microservice.AI/test/MS.Microservice.AI.Qwen.Tests：13 passed，见 [输出](qwen-tests.txt)。
  - 上述四个项目均 0 failed / 0 skipped。

覆盖容量 1/N、预取消/等待取消、异常作用域、重复/并发 Dispose、旧租约不影响后续持有者、AI 初始化失败后恢复、流式提前释放、响应释放异常及排队取消。测试通过内存 HTTP handler，不连接真实 AI 服务。子代理独立审查未发现明确缺陷。

## 限制与兼容
本机缓存恢复未进行漏洞审计；未验证真实 AI 服务或部署容量。调用方必须在所有等待者和租约结束后再 Dispose 信号量。租约本身不拥有信号量，不处理模型/连接关闭，也不提供公平排队、重试或持久队列。

先前 [Singleflight](../THREADING-SINGLEFLIGHT/summary.md) 已通过验收并提交为 22bdd68。
