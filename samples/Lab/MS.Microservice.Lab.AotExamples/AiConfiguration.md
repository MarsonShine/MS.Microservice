# AI 配置为什么保留泛型校验，却移出泛型绑定

原先 `ConfigureValidatedOptions<TOptions, TValidator>` 同时做 `AddOptions`、`Bind`、`ValidateOnStart` 和注册验证器。代码很短，但 `Bind` 所在位置只有未闭合的 `TOptions`。配置 Source Generator 需要在编译时知道真实 options 类型及其成员，无法为任意将来传入的类型预先生成绑定代码。

现在仍由泛型 helper 注册和验证，但返回 `OptionsBuilder<TOptions>`；每个能力入口在已知的具体类型上调用 `Bind`，同时在 AI.Core 启用 `EnableConfigurationBindingGenerator`。例如：

```csharp
var options = ConfigureValidatedOptions<AIRateLimitingOptions, AIRateLimitingOptionsValidator>(services);
if (section is not null) options.Bind(section);
```

这保留 .NET 标准绑定器的配置层级、默认值、集合和 reload 语义，不另写手工配置解析器。收益是能够生成静态绑定，属于启动配置阶段的 AOT compatibility；没有依据声称它显著提高请求吞吐量。回归覆盖六类生产配置、嵌套只读字典、大小写不敏感集合、默认值、reload 和验证错误。

DeepSeek 的限制校验还有另一条动态路径：每一项先 `typeof(T).GetProperty("Provider")` 再 `GetValue`。所有调用方实际都知道具体模型类型，传 `static model => model.Provider` 即可让编译器检查属性，并保留一个通用循环。`Legacy/AI/ProviderSelection.cs` 和 `Static/AI/ProviderSelection.cs` 保存独立对照，测试核对大小写、匹配与非匹配场景。此处不改变“DeepSeek 只支持 Chat”的业务规则。
