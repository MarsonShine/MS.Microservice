# Slice 02：有界响应与 SSE 读取

Status: complete

范围：AI.Core 的 Provider 响应读取、选项/校验、共享读取 helper；AI 相关测试与 README/设计文档；本任务状态/证据文档。范围外：Provider 接口重设计、输入预算、重试次数/排队超时、多实例配额。

根因：Chat/Media 的 ResponseContentRead 在解析前整包缓冲，错误正文完整读取，SSE ReadLineAsync 无应用级长度约束。不变量是每个成功/错误响应和每行 SSE 在合理配置预算内读取，超限必须终止、释放且不重试。

方案：headers-first、复用.NET受限缓冲，补已预缓冲内容实际长度校验；SSE 有界逐行读取保留取消、CR/LF/BOM/Unicode和首事件增量性。不将每响应预算宣传为进程总内存上限。

验收：`dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx --no-restore -c Release`。包括未知/伪造长度、预算边界、已预缓冲、流握手错误、SSE超长数据/注释、正文停顿取消/超时与首事件即时性。

结果：先在旧实现通过配置字符串设置 128 字节预算，重建后的超限 Chat 回归失败（应抛异常但未抛出），不是编译失败。实现后新增公开 DI 回归 27/27 通过；完整 AI 解决方案 184/184、0 失败、0 跳过，退出码 0（Core 109、OpenAI 31、Qwen 13、DeepSeek 10、QuestionGeneration 21）。Core 单独构建亦为 0 警告、0 错误。

复核：计数 sink 不存储或复制另一份正文，验证预缓冲内容实际长度；SSE 使用逐字符外部读取避免等待补满大块，framework继续负责解码。超限均映射不可重试 ResponseInvalid，响应和共享名额按原作用域归还。git diff --check 通过。

边界：预算不是进程总内存上限，也不能追溯阻止自定义 handler 交付响应前的分配；SSE按行限长、总持续时间由现有timeout控制，不新增总字节限制；每字符调用有CPU开销，未声明性能提升。未调用真实AI服务。
