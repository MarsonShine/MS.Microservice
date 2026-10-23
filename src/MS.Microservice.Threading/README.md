# Threading

仅依赖 .NET 的并发协调工具，包含批量处理、同键请求合并和异步名额租约。

## BatchProcessing
仅依赖 .NET，复用 Parallel.ForEachAsync。CollectAsync 接受稳定的有限 IReadOnlyList、异步处理函数和显式正整数 maxConcurrency，返回按输入索引排序的逐项结果（Index、Value、Error、IsSuccess）。

```csharp
var results = await BatchProcessing.CollectAsync<string, string>(paths,
    async (path, token) => await File.ReadAllTextAsync(path, token),
    maxConcurrency: 4, cancellationToken);
```

单项异常记入结果并继续；调用方取消停止派发，将令牌传入在途处理，等待在途处理结束后抛出取消，不返回不完整列表。与整体取消无关的单项取消记录为该项失败。处理函数必须并发安全；执行期间不得修改输入列表。处理函数忽略取消时，本方法必须等待它结束。

结果保存在内存，适合有限批次；无持久化、重试、队列或事务。遇错停止的简单场景可直接使用 Parallel.ForEachAsync。

## Singleflight

同一 `SingleflightManager` 实例内，按字符串键（区分大小写）和结果类型合并在途操作。只有第一个调用的工厂执行，所有等待者共享其结果、异常或操作取消；完成后再次调用会重新执行，不缓存结果。

```csharp
var flights = new MS.Microservice.Threading.SingleflightManager(); // 跨调用复用此实例
var result = await flights.ExecuteOnceAsync("resource:42", LoadAsync, cancellationToken);
```

`LoadAsync` 为调用方提供的 `Func<Task<T>>`。第三个参数只取消当前调用的等待（首个调用者也一样），不终止共享执行。工厂如需取消，应使用调用方明确管理的操作令牌，不应把某个等待者的请求令牌当作共享执行寿命。工厂同步返回 Task 前的代码不会被等待取消抢占。

同键不同结果类型互不合并。失败后可再次执行，记录只由本次操作的拥有者清理。Core 的 `MS.Microservice.Core.Concurrent.SingleflightManager.ExecuteOnceAsync<T>(string, Func<Task<T>>)` 保留为转发入口，原签名不变。

## 异步名额租约

`SemaphoreSlim.AcquireAsync` 等待取得一个名额，返回可用于 `using` 的租约。容量 1 可用于互斥，容量 N 可用于多次独立调用共享的并发上限。

```csharp
using MS.Microservice.Threading;

// gate 是调用方持有并跨请求复用的 SemaphoreSlim。
using var lease = await gate.AcquireAsync(cancellationToken);
await ExecuteAsync(cancellationToken);
```

离开作用域（包括异常或异步枚举器释放）归还名额。同一租约重复或并发 Dispose 只归还一次；等待取消且未获取名额时不会释放。成功取得名额后再发生取消，仍由租约负责释放，取消本身不会中止受保护操作。

工具不释放信号量，调用方须等所有等待者和租约结束后才能 Dispose 信号量。调用方不能再对同一份名额手工 Release。AI 的普通聊天、流式聊天和媒体请求复用此入口；流式请求在枚举器释放前一直占有名额。
