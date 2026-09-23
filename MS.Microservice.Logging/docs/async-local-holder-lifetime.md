# `AsyncLocal` 中何时需要 Holder

`MsRequestLoggingMiddleware` 在请求开始时调用 `RequestLogScope.Push`，在请求结束时释放作用域。假设请求内启动了一个继承执行上下文的子任务，但请求先结束，子任务随后才写日志：这条日志还应该通过 `RequestLogScope.Current` 读到已结束请求的 `RequestId` 吗？

本模块选择 **不应该**。请求作用域释放后，仍持有该作用域 ambient 状态的子流程，再读取 `Current` 应得到 `null`。这只约束通过 `Current` 查找的值；它不会取消子任务，也不能收回调用方先前保存的 `RequestLogContext` 引用。独立的后台工作应建立自己的日志作用域，或显式传递它需要的字段。

## 为什么恢复 `AsyncLocal.Value` 不够

`AsyncLocal<T>` 的值随 `ExecutionContext` 流向异步调用。子流程在启动时继承当时的值；父流程后来给自己的 `AsyncLocal.Value` 赋新值，不会改写子流程已经继承的值。[`AsyncLocal<T>` 文档](https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1)说明它保存异步控制流的 ambient 数据；[`ExecutionContext` 文档](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext)说明该上下文会跨异步点传递。

如果 `AsyncLocal` 直接保存 `RequestLogContext`，一次晚于请求结束的读取可能是这样：

```text
父流程 Push(C) ── 子流程继承 C ── 父流程 Dispose，恢复先前的值
                                    └─ 子流程仍能从自己的 AsyncLocal.Value 读到 C
```

`Dispose` 对父流程的恢复仍然正确，正常的 `await` 和嵌套作用域也仍然工作。缺口只在于：**已继承旧值的其他执行流不会因此失效**。即使 `RequestLogContext` 本身是可变对象，修改父流程的 `AsyncLocal.Value` 也没有改变子流程所持有的引用。

## 三种写法的区别

| `AsyncLocal` 保存什么 | 释放当前作用域时做什么 | 已继承该作用域的子流程随后读取 `Current` |
| --- | --- | --- |
| `RequestLogContext` | 当前流程恢复先前的引用 | 仍可能读到旧上下文 |
| 只有只读 `Context` 的包装对象 | 当前流程恢复先前的包装对象 | 仍可能通过旧包装对象读到旧上下文 |
| `Context` 可清空的 Holder | 先把当前 Holder 的 `Context` 设为 `null`，再恢复先前的 Holder | 如果它仍以该 Holder 为当前值，读到 `null` |

第二种正是本模块最初的 `ScopeState`：`Context` 只有 getter，`Dispose` 仅恢复 `priorState`，没有清空旧对象。因此它虽然多分配一个对象，却不具备第三种写法的跨流程失效语义。随后删除该包装对象，减少了分配，但也没有改变这一语义。真正起作用的是**多个执行流共享同一个可清空的间接引用**，并在作用域结束时清空它，而不是“使用了包装类”本身。

[ASP.NET Core 的 `HttpContextAccessor` 源码](https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http/src/HttpContextAccessor.cs)采用了这个间接引用：`AsyncLocal` 保存 `HttpContextHolder`；更换或清除当前 `HttpContext` 时，先将旧 Holder 的 `Context` 设为 `null`。这样，仍引用旧 Holder 的执行上下文再经 accessor 读取时也得到 `null`。

## `RequestLogScope` 应怎样使用 Holder

`HttpContextAccessor.HttpContext` 的 setter 是替换语义；`RequestLogScope.Push` 则允许嵌套，并承诺内层释放后恢复外层。因此不能在进入内层作用域时清空外层 Holder。正确的时序是：

1. `Push(outer)` 创建外层 Holder A；`Push(inner)` 保存 A，再创建内层 Holder B。此时 `Current` 读取 B 的上下文。
2. 释放内层作用域时，清空 **B** 的 `Context`，然后在当前流程恢复 A。父流程再次读到 `outer`；此前继承 B、且没有建立自己新作用域的子流程读到 `null`。
3. 释放外层作用域时，清空 **A** 的 `Context`，再恢复进入外层前的值。此前继承 A 的子流程此后也读到 `null`。

每次 `Push` 都需要自己的 Holder，包括把同一个 `RequestLogContext` 再次压栈的情况。否则内层释放时清空共享 Holder，会意外清掉仍有效的外层作用域。作用域应按进入顺序的逆序释放；这个方案没有定义乱序释放的栈行为。

Holder 只控制 **ambient 查找**。如果代码提前执行了 `var saved = RequestLogScope.Current`，之后直接使用 `saved`，清空 Holder 不能撤销这份引用。如果子流程已主动创建新的日志作用域，它读到的是自己的 Holder；父作用域的释放也不应清空那个新作用域。这里的失效规则不能代替任务生命周期管理或日志数据的访问控制。

## 什么时候直接存值

如果某个 ambient 值只需在当前异步流程中传递，并且允许已启动的子流程保留启动时继承的值，那么直接使用 `AsyncLocal<T>`、在退出时恢复先前值即可。例如，所有继承该值的任务都保证在作用域结束前完成，或者旧值在子任务中继续使用符合该值的生命周期约定。即便如此，也应通过并发测试确认嵌套恢复和子流程行为。

如果作用域结束意味着**所有仍引用该作用域 Holder 的执行流都应停止通过 ambient API 取得值**，就需要可清空的 Holder。仅为了模仿某个框架的类形状而加只读包装，没有这个效果。`ConfigureAwait(false)` 不等于阻止 `ExecutionContext` 流动，不能作为这里的失效手段；显式禁止上下文流动则会改变其他 ambient 数据的传递，不是本模块的局部解决办法。

## 本模块的取舍与验证边界

`RequestLogScope.Current` 是 NLog 和 Serilog 适配器共同读取的入口。HTTP 中间件在请求结束后释放其作用域，因此失效规则应由 `RequestLogScope` 实现，而不是分别交给两个日志后端处理。Worker 或消息处理代码手动调用 `Push` 时，同一规则也适用：释放该作用域后，晚到的继承流程不应继续借它取得日志字段。

此前针对**只读 `ScopeState` 与直接存值**的 Release 微基准显示，直接存值把普通 Push/Dispose 的分配从 128 B/次降到 104 B/次。改为可清空 Holder 后，同一[基准程序](../benchmarks/MS.Microservice.Logging.Core.Benchmarks/Program.cs)在 Windows x64、.NET 10.0.12、Release 下测得普通 Push/Dispose 为 117.3 ns、136 B/次，嵌套 Push/Dispose 为 139.3 ns、208 B/次。每场景 7 轮、每轮 20 万次，取中位数；独立重复运行的分配值相同，耗时有波动。相比直接存值，每次多分配 32 B。它只说明作用域操作的成本，不能推导完整 HTTP 请求或日志输出的吞吐量。

回归测试先在直接存值实现上复现了“子任务在父作用域释放后仍读到旧请求上下文”，随后验证了可清空 Holder 对子任务、捕获的 `ExecutionContext`、同一上下文嵌套、并行子流程及重复释放的行为；NLog、Serilog 和 HTTP 中间件测试也通过。这一选择只增加普通托管对象和静态泛型访问，不依赖反射或动态代码生成。多出的分配是为请求结束后的 ambient 失效约定付出的成本。
