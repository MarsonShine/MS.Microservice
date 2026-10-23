# ProcessRunner
仅依赖 .NET 的命令执行工具。RunAsync 接收可执行文件、参数列表、ProcessRunOptions 和取消令牌，返回 ExitCode、StandardOutput、StandardError。非零退出由调用方解释。

默认 UTF-8；两个输出流分别限制为 262144 字符，超限失败。默认执行期限 1 分钟、清理期限 10 秒。取消或失败时请求终止进程树；清理失败以 AggregateException 保留原始失败。进程树终止受操作系统能力限制，不保证所有后代退出。

`Timeout` 支持正期限或 `Timeout.InfiniteTimeSpan`；无限执行期限仍响应调用方取消。`CleanupTimeout` 必须为正的有限期限。有限期限按 .NET 计时器规则截取为整毫秒后，不得超过 4,294,967,294 毫秒；两项均在准备参数和启动进程前校验，非法配置抛出 `ArgumentOutOfRangeException`。

使用 ArgumentList，无 shell 展开；支持工作目录和环境变量覆盖（null 删除变量）。可执行文件和参数由可信调用方提供。此接口用于文本输出，不支持 stdin 协议或二进制输出。
