# Slice 05：请求最终结果日志

Status: complete

范围：Logging.AspNetCore 中间件及必要项目配置/单测、Logging README；ReferenceHost 与 Lab PlatformWebHost 管线顺序；Reference.Web.Tests；任务状态/证据。范围外：业务错误映射、授权规则、日志后端、响应正文记录。

根因：日志位于异常处理中间件内侧，finally在错误响应形成前读取200。不变量：已处理请求完成日志与最终响应一致；真正未处理失败和客户端取消不得伪装成功或猜测状态码。

方案：日志包围异常与timeout处理；正常和已处理错误保留原5000日志模板；未处理失败/取消分别记录明确结果，未开始响应时status为null，仅记录异常类型，仍传播原异常；所有路径释放ambient scope且尊重EmitCompletionLog。

验收：Logging.AspNetCore.Tests完整测试，以及Reference.Web.Tests当前源码重建。通过实际ReferenceHost组合管线产生409/500/504并比较完成日志；同时验证未处理异常、已开始响应、请求取消/取消后正常返回、禁用日志和scope释放。

结果：旧实现日志组件17项中7失败10通过，失败涉及异常/取消分类及Warning过滤；真实Reference新增409/500回归均得到错误日志200，504原路径通过。修复后源码重建Logging.AspNetCore.Tests 17/17、Reference.Web.Tests 76/76，均0失败0跳过、退出码0。Lab Release构建0警告0错误，git diff --check通过。

命令：
- `dotnet test MS.Microservice.Logging/test/MS.Microservice.Logging.AspNetCore.Tests/MS.Microservice.Logging.AspNetCore.Tests.csproj --no-restore -c Release`
- `dotnet test test/MS.Microservice.Reference.Web.Tests/MS.Microservice.Reference.Web.Tests.csproj --no-restore -c Release`
- `dotnet build samples/Lab/MS.Microservice.Lab/MS.Microservice.Lab.csproj --no-restore -c Release`

复核：正常/handled保留5000模板；未处理失败5001 Warning、请求取消5002 Info，未开始响应时StatusCode=null，只记录FailureType。每种结果遵循自身级别及EmitCompletionLog，原异常原样传播、scope清理。两宿主顺序同步调整，无公开API或依赖变化。

边界：旧日志查询若把所有结果都视为5000需按README接纳新事件；Lab验证为构建与共享中间件单测，不声称执行其完整真实管线。Reference测试中的机器DataProtection非致命警告未扩展为本片配置改动。
