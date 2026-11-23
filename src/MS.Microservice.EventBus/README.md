# MS.Microservice.EventBus

进程内发布／订阅。通过 `IEventBus` 订阅事件后，`await bus.PublishAsync(message)` 会等待该事件的处理器执行完毕。业务代码写在本包的 `IIntegrationEventHandler<TEvent>.Handle(TEvent)` 里。

## 运行第一个消费者

在 .NET 11 控制台项目中引用本模块或安装包含进程内实现的包。示例使用 Microsoft 的 DI 容器。

```shell
dotnet add package MS.Microservice.EventBus
dotnet add package Microsoft.Extensions.DependencyInjection
```

将下面的代码放进 `Program.cs`。运行后只会输出一次 `Consumed order 42`。

```csharp
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

var services = new ServiceCollection();
services.AddInMemoryEventBus();
services.AddScoped<OrderCreatedHandler>();
await using var provider = services.BuildServiceProvider();

var bus = provider.GetRequiredService<IEventBus>();
bus.Subscribe<OrderCreated, OrderCreatedHandler>();
await bus.PublishAsync(new OrderCreated(42));

bus.Unsubscribe<OrderCreated, OrderCreatedHandler>();
await bus.PublishAsync(new OrderCreated(43));

public sealed class OrderCreated(int orderId) : IntegrationEvent
{
    public int OrderId { get; } = orderId;
}

public sealed class OrderCreatedHandler : IIntegrationEventHandler<OrderCreated>
{
    public Task Handle(OrderCreated message)
    {
        Console.WriteLine($"Consumed order {message.OrderId}");
        return Task.CompletedTask;
    }
}
```

`AddScoped<OrderCreatedHandler>()` 让容器能按具体类型构造处理器。`Subscribe<OrderCreated, OrderCreatedHandler>()` 建立事件和处理器之间的绑定。首次发布执行 `Handle`，退订后的第二次发布没有匹配项，直接完成。

## 投递语义

- **路由**按发布对象的运行时 CLR 类型精确匹配。即使两个事件类都叫 `Changed`，各自注册的处理器也只接收对应的事件。同一短名下重复注册同一处理器类型会抛出 `ArgumentException`。订阅管理器按短名查询时返回首次绑定的类型，直到该名称的最后一个处理器注销。未知名称返回 `null`，空名称或纯空白名称会报错。
- **执行**使用发布开始时的订阅快照。总线在锁外按注册顺序逐个解析并等待处理器。处理中可以订阅或退订，这些变更对后续发布生效。
- **生命周期**为一次有匹配处理器的发布创建一个 DI 作用域。同一次发布中的处理器共享 scoped 依赖。发布结束时异步释放作用域，包含处理器抛出异常的路径。
- **失败**由发布方观察。一个处理器失败后，本次发布停止，异常传播给调用者。内存投递本身不提供重试、持久化或跨进程传输。
- **兼容入口**保留 `IEventBus.Publish(IntegrationEvent)` 和旧接口名 `IEventbus`。内置总线的 `Publish` 在线程池启动消费，调用线程同步等待结果并收到处理异常。异步代码使用 `await PublishAsync(...)`。已有同步适配器可以继续只实现 `Publish`。事件基类继续支持通过 `System.Text.Json` 回读原始 ID 和创建时间。

[两个消费者的完整示例](samples/basic-usage.md)展示了注册顺序、异步等待和 scoped 依赖。此前只测订阅注册、注销与按名称查类型的性能基线，见[订阅索引源码笔记](docs/subscription-performance.md)。

如果宿主已经按 `IEventBusSubscriptionsManager` 注册了内置管理器单例，`AddInMemoryEventBus()` 会使用该实例，直接调用它的 `AddSubscription`、`RemoveSubscription` 或 `Clear` 都会影响同一条消费链路。
