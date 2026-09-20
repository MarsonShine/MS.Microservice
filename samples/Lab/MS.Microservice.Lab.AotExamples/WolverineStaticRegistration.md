# 为什么 Wolverine 桥接要显式登记泛型？

## 原来把哪一步留到了运行时

完整原代码保存在 [`Legacy/Messaging/WolverineMessagingExtensions.cs.txt`](Legacy/Messaging/WolverineMessagingExtensions.cs.txt)。它在启动时遍历消息合同，按字符串找到自己的私有泛型方法，再用 `MakeGenericMethod(eventType, contextType).Invoke(...)` 配出每个事件的桥接类、别名和发布地址。

这属于 **AOT compatibility**：编译器只能看到类型变量，不一定会提前生成对应闭合泛型方法和桥接类型。它发生在启动配置中，本次没有据此宣称运行中的消息处理速度显著提高。

## 新写法为什么更直接

调用方本来就知道事件类型和数据库上下文，因此可以直接写出组合：

```csharp
builder.Host.UseWolverineMessaging<AppDbContext>(topology, settings,
    [WolverineMessageRegistration<AppDbContext>.For<ProfileChanged>()]);
```

`For<TEvent>` 创建一个静态委托，委托里直接引用 `WolverineIntegrationEventHandler<TEvent, TContext>` 和 `PublishMessage<TEvent>()`。配置阶段只调用这个委托，不需要按名称查方法、拼装泛型或反射执行。合同名和版本仍从 `topology` 读取，所以没有增加另一份别名配置。

显式登记最容易漏掉一个事件，因此配置入口先检查登记与合同完全对应：缺失、重复或额外事件都会在修改 Wolverine 选项前失败。空拓扑接受空登记。

## 处理器发现为什么没有简单删除

中立的 `IIntegrationEventHandler<T>` 必须只经过适配器调用，否则 Wolverine 按惯例发现同一业务处理器后可能绕过桥接。现在泛型接口继承不带泛型参数的标记接口，排除规则只检查这个标记，不再逐个枚举类型接口。未注册和继承而来的中立处理器仍被排除；普通本地命令处理器仍可发现。

## 对照与验证边界

[`Legacy/Messaging/WolverineBridgeExample.cs`](Legacy/Messaging/WolverineBridgeExample.cs) 提取原来的反射注册核心；[`Static/Messaging/WolverineBridgeExample.cs`](Static/Messaging/WolverineBridgeExample.cs) 独立实现对应的直接泛型调用。测试检查实际创建的发布端点、订阅、持久化模式和真实桥接发现结果，不启动数据库或消息代理。

生产组件测试还验证缺失、重复、额外和多事件登记，并保留四种中立处理器形态与普通本地命令的发现测试。这里移除的是自有代码的动态泛型装配；Wolverine 自身的发现与生成机制仍属于第三方实现，整个宿主的 NativeAOT 发布兼容性没有在这一步得到证明。
