# AI 配置：为什么把 Bind 挪出来，为什么给 Provider 传一个 Lambda

假设配置里写了 `AI:RateLimiting:RequestsPerWindow = 3`。程序需要读取这个值，把它转成整数，再填进 `AIRateLimitingOptions.RequestsPerWindow`。这就是这里说的“配置绑定”。

这次修改没有改变配置的写法。它调整的是：由谁决定要给哪个属性赋值，以及这个决定是在编译时还是运行时完成。

## 先看 Bind 为什么换了位置

原来的辅助方法把注册、绑定和校验放在一起。省略校验部分后，结构是这样：

```csharp
private static void ConfigureValidatedOptions<TOptions>(
    IServiceCollection services, IConfigurationSection section)
    where TOptions : class
{
    services.AddOptions<TOptions>().Bind(section);
}
```

调用时确实会指定 `AIRateLimitingOptions`，但 `Bind` 这行写在泛型方法内部。在分析这行源码时，配置生成器看到的目标类型仍是 `TOptions`，无法据此列出需要绑定的属性。它不会沿着所有调用方，把这个辅助方法分别展开成限流版、熔断版、日志版，再为每一版生成绑定代码。

所以，仅仅打开 Source Generator 开关还不够。这个调用位置无法生成对应的绑定代码，仍会走需要运行时类型信息的绑定方式。对 NativeAOT 来说，这会带来保留属性等元数据的要求。

现在把 `Bind` 放回知道具体类型的方法里：

```csharp
// 位于 AddAIRateLimiter 内部。
var options = ConfigureValidatedOptions<
    AIRateLimitingOptions, AIRateLimitingOptionsValidator>(services);

if (section is not null)
    options.Bind(section);
```

虽然用了 `var`，这里 `options` 的编译期类型已经是 `OptionsBuilder<AIRateLimitingOptions>`。生成器分析 `Bind` 时，能找到 `RequestsPerWindow`、`WindowSeconds` 等具体属性，也就能生成读取配置和赋值的代码。

生成代码做的事，大致相当于下面这样。这只是说明原理的简化示例；实际代码还处理空值、转换失败和配置路径等情况：

```csharp
var text = section["RequestsPerWindow"];
if (text is not null)
    instance.RequestsPerWindow = int.Parse(text);
```

配置值仍然在程序运行时读取。编译时提前生成的是“怎样读取并赋值”的代码，配置中的 `3` 并没有被写死进程序集。

[AI.Core 项目文件](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/MS.Microservice.AI.Core.csproj)中的开关是：

```xml
<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
```

这里使用 .NET 提供的配置绑定生成器，项目不需要自己写一个 SG。它为支持的 `Bind` 调用生成实现，并让这些调用使用生成代码。调用位置能够确定具体类型，是这里需要满足的条件。

通用辅助方法还保留着，因为注册和校验仍然可以共用：`AddOptions<TOptions>()` 注册配置对象，`ValidateOnStart()` 安排启动时校验，`IValidateOptions<TOptions>` 对应具体的验证器。这次只把需要生成器识别具体属性的 `Bind` 移出去了。完整代码见 [AIServiceCollectionExtensions.cs](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/AIServiceCollectionExtensions.cs)。

## 调用方需要改什么

应用原来的注册方式可以继续使用：

```csharp
services.AddMicroserviceAI(configuration);
```

它会读取 `AI` 配置节，再分别注册限流、熔断等配置。调用方不需要手写每个字段的赋值，也不需要为了这次修改另外创建映射表。

这里继续使用标准 Options 接口。现有测试检查了几个容易在替换绑定方式时出错的行为：未配置 `WindowSeconds` 时保留默认值 `60`；嵌套字典和集合保留大小写不敏感的查询方式；配置重载后，`IOptionsMonitor` 能读到从 `3` 改成 `5` 的新值；把每窗口请求数设为 `0`，仍会抛出包含原错误信息的 `OptionsValidationException`。六类配置都检查了实际绑定结果，详见 [配置绑定测试](../../../MS.Microservice.AI/test/MS.Microservice.AI.Core.Tests/GeneratedConfigurationBindingTests.cs)。

这部分主要解决 **AOT compatibility**。配置对象创建或重载时可以使用生成代码，减少对运行时属性发现的依赖；它没有改变每次 AI 请求的处理过程，也没有请求吞吐量的测量结果。

## DeepSeek 的 Provider 读取为什么不用 SG

同一个提交还修改了 [DeepSeekOptionsValidator](../../../MS.Microservice.AI/src/MS.Microservice.AI.DeepSeek/DeepSeekOptionsValidator.cs)。本模块的 DeepSeek 适配器只注册聊天能力，因此校验配置时，需要检查 TTS、ASR、图片生成和图片编辑是否误填了 `Provider = "DeepSeek"`。

这几类配置的模型类型不同，原来的代码为了共用一个循环，通过属性名读取 Provider：

```csharp
foreach (var model in models)
{
    var property = typeof(TModelOptions).GetProperty("Provider");
    var provider = property?.GetValue(model.Value) as string;
    // 接着判断 provider 是否为 DeepSeek。
}
```

这样每检查一项配置，都要先按字符串查找属性，再通过反射取值。而调用这个方法的地方已经知道模型的具体类型，可以直接写 `model.Provider`。

因此，现在由调用方传入读取方式：

```csharp
AddUnsupportedCapabilityFailures(
    options.Models.Tts,
    static model => model.Provider,
    failures,
    "Tts");
```

辅助方法接收 `Func<TModelOptions, string?> getProvider`，循环中改为：

```csharp
var provider = getProvider(model.Value);
```

`static model => model.Provider` 是一个普通委托：给它一个模型，它返回该模型的 Provider。属性名和类型由编译器检查；如果这个模型没有 Provider 属性，代码就无法编译。`static` 表示这个 Lambda 不能捕获外部局部变量，它仍然接收参数，也仍然要在运行时调用。

这段代码只需要读取一个已知属性。传一个委托就能复用循环，并且去掉 `GetProperty` 和 `GetValue`，所以没有必要再为它编写生成器。前面的配置绑定涉及多种属性、类型转换和嵌套集合，而且 .NET 已经提供了生成器，使用现成实现更合适。

这消除了校验循环里逐项执行的反射，但仍有普通委托调用。它发生在配置校验阶段，不能据此声称聊天请求会明显变快。Provider 仍按忽略大小写的方式比较；对本模块不支持的能力，报错规则和配置路径保持原样。

想单独比较这两种读取方式，可以打开学习区的 [旧实现](Legacy/AI/ProviderSelection.cs)、[新实现](Static/AI/ProviderSelection.cs)和[对照测试](../../../test/MS.Microservice.Lab.AotExamples.Tests/ProviderSelectionExamplesTests.cs)。对照测试使用相同输入，覆盖 `DeepSeek`、`deepseek`、其他 Provider 和 `null`；[生产校验测试](../../../MS.Microservice.AI/test/MS.Microservice.AI.DeepSeek.Tests/DeepSeekCapabilitySupportTests.cs)则检查四种非聊天能力的报错。
