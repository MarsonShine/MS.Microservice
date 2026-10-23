# 重试执行契约
基线 b4b9394。范围见 [任务契约](task.md)。保留旧接口/Builder/策略条件，新增 ExecuteCancellableAsync，避免旧 null 调用产生重载歧义。

根因：旧token只被正延迟等待观察；耗尽重新throw异常丢失堆栈；同步阻塞的内部await捕获同步上下文。修复在共享执行层检查取消、传令牌、使用EDI及ConfigureAwait(false)，同时正确填写LastException与尝试日志。旧WithTimeout、N额外重试、独立OCE条件和结果耗尽语义保留。

验收命令：dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet。
基线49 passed，见 [基线输出](baseline.txt)；修改后62 passed / 0 failed / 0 skipped，见 [验收输出](tests.txt)。回归覆盖预取消、零延迟/等待取消、两种令牌委托、异常身份与堆栈、同步上下文、扩展调用、结果耗尽和WithTimeout。测试夹具Queue名称冲突已修正。

子代理只读复查未发现明确缺陷。未运行全仓或真实服务验证；不改HTTP、AI、Networking，本地提交不推送。
