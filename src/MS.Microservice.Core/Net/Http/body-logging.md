# HTTP 正文日志为什么不用自定义内容包装器

`LoggingHttpClientHandler` 曾给请求和响应都套上 `LoggableHttpContent`，再由 `LazyContentLogger.ToString()` 在日志格式化时读取正文。包装器要保存字节、复制 headers、重新实现内容写出和释放；日志关闭时也会被安装。若正文尚未读完，`ToString()` 的同步等待会占住执行日志格式化的线程。

现在 Handler 在 Information 日志关闭时直接调用下一层，不触碰内容。开启完整正文日志时，先异步调用原 `HttpContent.ReadAsStringAsync`，记录得到的字符串，再发送请求或交付响应。[.NET 的 API 说明](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcontent.readasstringasync?view=net-10.0)明确指出这次读取会在内容对象内部缓冲字节；后续同一对象仍可发送或读取。保留原对象也保留了它的 headers、字符集及调用方的释放责任。响应尚未交付时若日志读取或提供器抛错，Handler 会先释放响应。

`EnableRedaction=true` 走独立路径：只记录方法、去查询参数后的路径、状态或失败类型、耗时，不读取正文。该设置默认关闭，保持已有完整 URI 和正文日志行为。完整正文日志仍会缓冲整个正文，也可能记录敏感数据；部署时需按数据要求决定是否启用脱敏。路径本身可能含业务标识，选项不会改写路径段。

原先公开的 `LoggableHttpContent` 和 `LazyContentLogger` 嵌套类型已从生产 Handler 移除。仓库内只有旧测试直接引用它们；仓库外若直接构造这两个类型，需要改为使用标准 `HttpContent` 的异步读取/复制 API。Handler 的单参数构造函数及 `EnableRedaction` 配置方式保持可用。

[微基准](../../../../benchmarks/MS.Microservice.Core.HttpLogging.Benchmarks/README.md)显示 Information 关闭时的托管分配下降；完整正文日志的分配也下降，但耗时在独立运行中波动，不能据此声称请求延迟改善。该基准使用内存 Handler，不含真实网络。回归测试覆盖关闭日志的流式转发、开启正文日志的异步等待、UTF-8/UTF-16、headers、内容所有权、取消、日志提供器失败，以及脱敏模式的正文不读取。

这次改动没有增加反射或动态代码。需要在保持流式转发的同时记录有界正文摘要时，必须另行设计读取方式；当前完整正文模式仍要先读完正文。
