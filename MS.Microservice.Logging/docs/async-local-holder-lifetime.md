# 请求结束后，日志为什么还拿得到旧 RequestId

一次 HTTP 请求带着 `RequestId = "req-42"` 进入 `MsRequestLoggingMiddleware`。处理请求时排了一个任务，任务要等请求结束后才读取日志上下文。请求已经结束，这个任务还应从 `RequestLogScope.Current` 读到 `req-42` 吗？本组件选择让它读到 `null`，避免把已结束请求的字段带到后续日志。

下面的代码可放在一个 `async Task` 方法中运行。`Task.Run` 在请求作用域内排队；`release` 保证读取发生在作用域释放之后：

```csharp
var release = new TaskCompletionSource<bool>(
    TaskCreationOptions.RunContinuationsAsynchronously);
Task<string?> lateLog;

using (RequestLogScope.Push(new RequestLogContext { RequestId = "req-42" }))
{
    lateLog = Task.Run(async () =>
    {
        await release.Task;
        return RequestLogScope.Current?.RequestId;
    });
    Console.WriteLine("作用域内：" + RequestLogScope.Current?.RequestId);
}

Console.WriteLine("作用域结束：" + (RequestLogScope.Current?.RequestId ?? "<null>"));
release.SetResult(true);
Console.WriteLine("已排队任务：" + ((await lateLog) ?? "<null>"));
```

当前实现的输出是：

```text
作用域内：req-42
作用域结束：<null>
已排队任务：<null>
```

前两行在三种实现下相同。最后一行则不同：

| `AsyncLocal` 保存的值 | “已排队任务”输出 |
| --- | --- |
| 直接保存 `RequestLogContext` | `req-42` |
| 保存只读的 `ScopeState { Context }`，释放时只恢复旧值 | `req-42` |
| 保存可清空的 Holder，释放时把 `Context` 设为 `null` | `<null>` |

调用 `Task.Run` 的那一刻，.NET 默认把当时的 `AsyncLocal` 值随 `ExecutionContext` 交给任务；任务实际何时开始运行不影响这一点。请求代码结束时改写自己的 `AsyncLocal.Value`，不会改写任务已经带走的值。只读 `ScopeState` 也一样：任务带走的是那个包装对象，包装对象里的 `Context` 从未被清空。修复前，`Dispose_ShouldClearContextInChildFlowCapturedBeforeDisposal` 测试实际读到了 `req-42`；现在它读到 `null`。

可清空的 Holder 给请求代码和已排队任务提供同一个间接引用。请求结束时清空这个对象里的 `Context`；任务随后通过 `Current` 查找，就看不到旧请求了。[ASP.NET Core 的 `HttpContextAccessor`](https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http/src/HttpContextAccessor.cs)也利用这层间接引用清理已结束的 `HttpContext`。[`ExecutionContext` 文档](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext)说明异步代码如何继承执行上下文。

## 请求中还有一个临时作用域

日志作用域可以嵌套。假设请求 ID 是 `request`，其中一小段操作临时使用 `step`；操作结束后，请求代码还要继续使用 `request`。这段操作也排了一个稍后才读取日志上下文的任务：

```csharp
var releaseStep = new TaskCompletionSource<bool>(
    TaskCreationOptions.RunContinuationsAsynchronously);
Task<string?> lateStepLog;

using (RequestLogScope.Push(new RequestLogContext { RequestId = "request" }))
{
    using (RequestLogScope.Push(new RequestLogContext { RequestId = "step" }))
    {
        lateStepLog = Task.Run(async () =>
        {
            await releaseStep.Task;
            return RequestLogScope.Current?.RequestId;
        });
    }

    Console.WriteLine("临时作用域结束：" + RequestLogScope.Current?.RequestId);
    releaseStep.SetResult(true);
    Console.WriteLine("已排队任务：" + ((await lateStepLog) ?? "<null>"));
}
```

输出是：

```text
临时作用域结束：request
已排队任务：<null>
```

请求代码得到 `request`，因为临时作用域结束后恢复了先前的作用域。任务排队时继承的是 `step` 作用域，而它在任务读取前已经结束。直接存值或旧的只读 `ScopeState` 会让第二行输出 `step`。

每次 `Push` 因此创建一个新的 Holder。进入 `step` 时保留 `request` 的 Holder；离开 `step` 时只清空 `step` 的 Holder，再恢复 `request`。即使两次 `Push` 传入同一个 `RequestLogContext` 对象，也要分别创建 Holder，否则结束内层作用域会让仍有效的外层作用域一起失效。`HttpContextAccessor` 的属性 setter 是替换操作，没有这里的嵌套恢复要求，不能把它的清理顺序直接照搬过来。

## 什么时候不需要 Holder

如果排出的任务本来就应该继续保留当时的值，直接使用 `AsyncLocal<T>` 即可。例如一个批处理任务在启动时继承不可变的批次标识，启动代码随后清空自己的值，任务仍读到原标识：

```csharp
var batchId = new AsyncLocal<string?> { Value = "batch-7" };
var release = new TaskCompletionSource<bool>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var work = Task.Run(async () =>
{
    await release.Task;
    return batchId.Value;
});

batchId.Value = null;
release.SetResult(true);
Console.WriteLine("启动代码：" + (batchId.Value ?? "<null>"));
Console.WriteLine("已排队任务：" + await work);
```

输出是：

```text
启动代码：<null>
已排队任务：batch-7
```

如果所有使用该值的任务都会在作用域结束前完成，直接存值也能满足这个生命周期约定。需要让任务长期保留某个字段时，显式把字段作为参数传给任务通常更容易看清所有权。

`RequestLogScope` 的值来自一次请求，`Dispose` 表示这次请求的日志作用域结束，因此选择可清空的 Holder。只增加一个不能清空的包装对象，对上面任何输出都没有帮助。

## 这项保证的边界和成本

Holder 控制的是**以后通过 `RequestLogScope.Current` 进行的查找**。如果代码在释放前执行 `var saved = RequestLogScope.Current`，之后直接使用 `saved`，Holder 无法收回那个引用。任务自行建立的新日志作用域也不会随原请求的作用域一起清空。调用方仍应按进入顺序的逆序释放嵌套作用域。

从模块根目录运行 [基准程序](../benchmarks/MS.Microservice.Logging.Core.Benchmarks/Program.cs)，在 Windows x64、.NET 10.0.12、Release 下，每场景测 7 轮、每轮 20 万次，取中位数：

| 普通 Push/Dispose | 耗时 | 托管分配 |
| --- | ---: | ---: |
| 直接保存上下文 | 100.8 ns/次 | 104 B/次 |
| 可清空 Holder | 117.3 ns/次 | 136 B/次 |

重复运行中分配差值稳定为 32 B/次，耗时有波动。这个基准只测作用域操作，不代表完整 HTTP 请求或日志输出的成本。该实现没有引入反射或动态代码生成。
