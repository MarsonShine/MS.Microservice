# 通用重试

RetryBuilder、RetryExecutor、IRetryStrategy 和 IRetryCondition 保持现有命名空间与签名。策略和条件的多次设置仍覆盖前一次；成功结果调用 ShouldRetry<T>，异常调用 ShouldRetry<object?>。自定义策略的 RetryContext.Attempt 从 1 开始，LastException 提供当前失败。

`RetryContext.TotalElapsed` 使用系统单调时间戳计算累计耗时，不受 UTC 校时影响。`StartTime` 保留创建时的 UTC 诊断时间，不用于持续时间判断。

## 次数和默认值

- maxRetries=N 是额外重试 N 次，最多调用 N+1 次；0 表示仅执行一次。
- 固定延迟默认为 0；指数策略支持 multiplier，默认最大延迟为 5 分钟。
- WithTimeout 在失败后按累计时间决定是否继续，不取消单次操作，也不是硬性总超时；默认等待 1 秒（传 default/0 同样采用默认等待）。
- 结果被条件持续拒绝时，耗尽仍抛 InvalidOperationException("Result does not meet retry condition")。
- Builder.OnRegexMatch 的 shouldMatchToRetry 默认 false；直接构造 RegexMatchRetryCondition 默认 true。保持这些既有默认值。

## 取消与异常

旧 ExecuteAsync(Func<Task<T>>, token) 及无结果版本保留：token 可阻止开始、后续尝试、延迟等待，以及取消后返回成功；旧委托没有令牌参数，正在执行的操作需要自行配合取消。

新 ExecuteCancellableAsync(Func<CancellationToken, Task<T>>, token) 及无结果版本将同一调用方令牌传给每次操作。采用不同方法名，避免旧的 null 委托调用产生重载歧义。

明确的调用方取消优先于重试条件；如果调用方没有取消，操作自身抛出的 OperationCanceledException 仍由旧条件决定是否重试。耗尽抛出最后一次异常并保留其原始堆栈。同步 Execute 仍阻塞到完成，执行器的异步等待不捕获同步上下文；不通过 Task.Run 改变调用入口。

普通 HTTP 使用独立 Http.Resilience 管线；AI 的 HTTP 分类与 Retry-After 逻辑不由本工具接管。

## 函数式兼容入口

`F.Retry<T>(retries, delayMillis, start)` 保留原签名，转发到同一 RetryExecutor，使用两倍指数间隔并显式关闭五分钟默认封顶。retries 表示额外尝试次数，非正值仅执行一次；无调用方令牌时仍会重试操作自身的取消异常。该入口不再递归执行重试。

`delayMillis` 接受 0 或正数；负数在调用操作前抛 ArgumentOutOfRangeException，避免旧 -1 意外无限等待或整数倍增溢出后的负延迟。合法的倍增计算使用 TimeSpan 范围，不再按 int 溢出回绕。

指数策略对有限 multiplier 的计算先应用 maxDelay，再转换 TimeSpan，巨大指数也能正确封顶；零基准延迟保持零。默认五分钟封顶和 multiplier 为 0／小于 1 时的序列保留。显式 maxDelay=TimeSpan.MaxValue 时，封顶仅受 TimeSpan 表示范围限制。

大于 .NET 单个定时器上限的合法正延迟会分段等待，每段都响应调用方取消，不会因等待太长提前开始下一次操作。WithTimeout 仍只在失败后判断是否继续，不会被改成硬截止时间；没有令牌的 F.Retry 不能中断已经开始的长等待。
