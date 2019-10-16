# 领域命令模式处理程序

在上篇博客中，为了响应请求，我们看到了领域命令模式的验证模式，“命令对象应该（总是活绝不）返回值“。这个问题有一个假设，那就是我们有命令（Command）对象。

在这篇文章中，我想看领域命令处理程序的一些选择：

![](asserts/Picture7.png)

当我们看到命令处理程序的时候，我实际上讨论的是关于请求处理的“实质”。这是转变状态的一部分。在很小的或者是很简单的一个应用程序中，我能直接将这个“改变”放进请求处理程序当中（比如控制器的 action 或者是 UI 状态事件处理程序）。

但是对于我构建的大部分系统来说，将所有的东西放到我应用程序的这边，这太乱了。这回导致一个问题 —— 这段逻辑应该去哪？我们能参照一些设计模式（包括命令对象模式 Command Object Pattern）。我有一个改变状态的代码块，并且我需要决定把它放哪。

## 静态帮助类/管理类/服务函数类

一个非常简单的选择就是创建静态类来占有这些可变函数：

```c#
public static class SomthingManager {
  public static void DoSomthing(SomthingRequest request, MyDbContext db)
  {
    // 领域逻辑放在这
  }
}
```

如果我们的方法需要在其它任何对象中做好它的工作，那么这些对象都会作为一个方法参数传递。我们不应该使用静态服务来定位。但是使用这个方法，我们可以使用各种函数技巧围绕这个简单的模式构建丰富的内容。

取决于你怎样打断这些方法到独立的类中单独的方法。你可能开始为每一个对象用一个静态类，或是每一个领域对象一个静态类，或是每个函数区域，又或是每个请求。尽管 C# 不支持 F# 的丰富的完整的功能，但是静态函数提供了一个合适的选择。

这个解决方法的优势是，它完整明显的表示出这段逻辑是做什么的。上面返回的类型是 void，但是我们只要看到了使用验证选择，那么它就会返回各种类型。

## DDD 服务类

DDD 服务模式与静态类有些略微的不同。最大的不同是 DDD 服务类是一个实例化对象，以及时常用于依赖注入。还有一个大的不同是，我认为服务类更加面向实体或聚合：

```c#
public static SomthingService: ISomthingService {
  pubnlic SomthingService(MyDbContext context) {
    _db = db;
  }
  
  public void DoSomthing(SomthingRequest request) {
    // 领域逻辑在这
  }
}
```

在 DDD 的世界中，服务类应该被设计在写作活动周围。之后，原来定义的服务类被设计在与聚合之间的协作或是聚合和内部服务的协作。但是那不是我通常看到的，我通常能看到 Java Spring 风格式的服务的地方我们有一个实体 Foo，那么：

- FooController
- FooService
- FooRepository

我将会极力阻拦这种服务，因为我们已经介绍了太多没有太多价值的任意分层。如果我们做对了 DDD，那么服务将会很少，因此在我们的系统中不需要每个命令一个服务。

## 指定请求处理程序

对于服务类和管理两种选项，我们通常看到多个请求在相同类的多个方法被处理。尽管，这没有什么能阻止你每请求一个服务，请求处理程序最终完成的目标相同：单个类和方法处理每一个独立的请求：

```c#
public class SomthingRequestHandler: IRequestHandler<SomthingRequest> {
    public void Handle(SomthingRequest quest){
      
    }
}
```

一个请求处理程序有各种方式：同步/异步，返回值或不返回值（void）以及组合它们。这是领域命令处理程序的默认选择，它促使我从每个请求和其它请求中分开。

// TODO





















translated from https://jimmybogard.com/domain-command-patterns-handlers/