# DynamicallyAccessedMembers：告诉裁剪器哪些成员还会被用到

这个特性解决的是一个发布问题：程序没有直接写 `new 某个类型(...)`，但 DI 容器稍后要根据类型信息创建它。裁剪器如果无法确认这条使用关系，就可能无法正确保留需要的构造函数。

`DynamicallyAccessedMembers` 可以理解为“对这个类型，后续代码需要动态访问哪些成员”。写在泛型参数上，就是给传入的具体类型提出成员保留要求。它不会生成构造函数，也不会把反射调用自动改成静态调用。

## 先理解裁剪为什么需要这些信息

普通构建通常会保留程序集中的大量代码。启用裁剪后，发布工具会分析应用实际使用了哪些类型和成员，尝试移除未使用的部分，以减少发布体积。构造函数、方法、属性信息都可能受到影响。

如果代码直接写：

```csharp
var options = new AIRateLimitingOptions();
```

工具能看到这个构造函数被调用了。但通过 DI 注册类型时，业务代码可能只写：

```csharp
ServiceDescriptor.Singleton<IQuestionDefinition, MyDefinition>();
```

随后由容器检查实现类型的公开构造函数、解析参数依赖，再创建对象。引用了 `MyDefinition` 这个类型，不等于它的所有成员都应该保留；框架需要声明自己会用哪些成员。

分析器能识别一些反射模式，也会利用框架的标记，所以不能认为只要出现反射就一定裁剪失败。这里的问题出现在自己的泛型包装方法没有传递框架的要求。关于裁剪如何分析这种关系，见 [微软的裁剪分析说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts)。

## 从 AddDefinition 这一处读起

[题目注册入口](../../../MS.Microservice.AI/src/MS.Microservice.AI.QuestionGeneration/DependencyInjection/QuestionGenerationServiceCollectionExtensions.cs)原来是：

```csharp
public QuestionGenerationBuilder AddDefinition<TDefinition>()
    where TDefinition : class, IQuestionDefinition
{
    Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IQuestionDefinition, TDefinition>());
    return this;
}
```

`where TDefinition : class, IQuestionDefinition` 只保证它是实现该接口的引用类型。它没有表达“发布时需要保留这个类型的公开构造函数”。

而 .NET 的 `ServiceDescriptor.Singleton<TService, TImplementation>()` 已经在 `TImplementation` 上声明了 `PublicConstructors` 要求。把未标注的 `TDefinition` 传进去，裁剪分析器就会指出：下层需要构造函数保留保证，上层却没有提供。这类泛型参数要求不匹配对应 [IL2091](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/il2091)。警告表示存在无法证明安全的地方，不代表每次运行都已经失败。

因此，这个提交把签名改为：

```csharp
public QuestionGenerationBuilder AddDefinition<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TDefinition>()
    where TDefinition : class, IQuestionDefinition
```

方法体没有改变。标记让分析器知道：任何传到这里的类型，都需要保留公开实例构造函数，供下层 DI 使用。

当调用方写 `builder.AddDefinition<MyDefinition>()` 时，类型已经确定，发布工具就能把要求落实到 MyDefinition。若调用方又包了一层 `Register<T>()`，并把 T 传给 AddDefinition，那么这一层也需要传递对应要求。

```text
业务调用 AddDefinition<MyDefinition>()
    ↓ 具体类型是 MyDefinition
自己的 AddDefinition<TDefinition>()
    ↓ 声明并传递 PublicConstructors 要求
框架 Singleton<IQuestionDefinition, TDefinition>()
    ↓ 注册该实现类型
DI 在创建实例时使用其公开构造函数
```

这就是原说明中“把契约向调用方传递”的具体含义。对于可达的这条调用链，工具能确定最终应保留哪个类型的哪些成员，不需要无差别保留整个程序集。

## 这个提交为什么用了两种保留范围

[AI 配置注册辅助方法](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/AIServiceCollectionExtensions.cs)中有两种不同需求：

```csharp
private static OptionsBuilder<TOptions> ConfigureValidatedOptions<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions,
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
    IServiceCollection services)
    where TOptions : class
    where TValidator : class, IValidateOptions<TOptions>
{
    var builder = services.AddOptions<TOptions>();
    builder.ValidateOnStart();
    services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
    return builder;
}
```

| 泛型参数 | 标记含义 | 本方法中承接的框架要求 |
|---|---|---|
| TOptions | 保留公开的无参实例构造函数 | ValidateOnStart 触发 Options 的创建与校验，其泛型参数声明了这个要求 |
| TValidator | 保留公开实例构造函数，包含有参构造函数 | Singleton 按实现类型注册，由 DI 创建验证器 |
| TDefinition | 保留公开实例构造函数，包含有参构造函数 | 同样由 Singleton 注册题目定义，DI 可以解析构造函数参数 |

这里不能因为当前某个验证器恰好没有构造参数，就把通用入口改成只保留无参构造函数。它接收的是可扩展的实现类型，应该满足实际调用的 DI API 所声明的要求。对应源码是 [.NET 10 的 ValidateOnStart](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Options/src/OptionsBuilderExtensions.cs)和 [ServiceDescriptor.Singleton](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ServiceDescriptor.cs)。

这些标记也不会凭空补齐构造函数。如果实现类型本来就无法由容器创建，或者它需要的依赖没有注册，仍然可能报错。`PublicParameterlessConstructor` 不等同于 C# 的 `where T : new()`：前者表达裁剪保留需求，后者是编译器检查的类型约束。

## 它与前面用过的 Source Generator 有什么区别

配置绑定生成器会生成直接访问属性的代码，再让支持的调用使用这些代码。`DynamicallyAccessedMembers` 不做这件事，它保留原执行方式所需要的成员及相关元数据。

| 做法 | 实际改变了什么 |
|---|---|
| Source Generator | 提前生成具体代码，减少运行时发现成员的需要 |
| DynamicallyAccessedMembers | 声明动态访问需要保留哪些成员，让工具检查调用链并据此保留 |
| 直接忽略裁剪警告 | 不再显示警告，本身没有增加成员保留保证 |

因此，原文“没有回到动态成员发现”容易让人误会成 DI 已经不用查看构造函数。准确地说，这个提交没有新增程序集扫描，但仍使用框架按已注册类型创建对象的机制。类型在调用处明确，所需构造函数也明确，工具可以分析这项需求。

## 为什么不全部标 All，或者直接改成工厂

保留 All 会扩大成员保留范围及其相关依赖，可能增加发布体积；它也不能修复任意动态代码生成或未知类型加载的问题。应当根据实际下游操作选择范围，而不是看到警告就保留全部成员。

直接注册一个包含 `new MyDefinition(...)` 的工厂也是可行的，工具更容易看到具体构造调用。但调用方就要明确提供对象的创建方式和依赖获取方式。本次保留了标准 DI 的构造函数注入，补齐它要求的标记，没有把注册接口全部改为工厂。这项修改属于 **AOT compatibility**，没有依据将其描述为请求性能提升。

标记也不是“相信我”的万能开关。如果类型来自运行时任意输入，工具仍可能无法证明其满足要求；只添加特性不能让未知类型自动变得可分析。NativeAOT 下能否运行还取决于其他代码、依赖和实际发布结果，不能用这几个标记代替整个应用的发布验证。

同一提交中 JsonArray 的强制转换是另一件事：它用于选择节点重载，避开泛型序列化入口，与构造函数保留无关。两项修改的上下文见 [AI JSON 元数据说明](AiJsonMetadata.md)。
