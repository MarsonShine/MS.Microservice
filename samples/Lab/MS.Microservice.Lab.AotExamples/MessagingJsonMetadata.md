# 消息类型登记为什么还需要 JSON 元数据？

## 旧机制缺少哪一部分

[`Legacy/Messaging/MessageContractRegistry.cs`](Legacy/Messaging/MessageContractRegistry.cs) 保存原实现。它登记 `Type`、合同名和版本：这能限制允许接收的消息类型，却没有告诉序列化器如何读取构造函数和属性。随后调用 `JsonSerializer.Serialize(message, type, options)` 时，默认解析器仍要发现这些成员。

这属于 **AOT compatibility**。旧配置已在注册表内复用，不能说每条消息都重新反射；问题在于裁剪后所需成员不一定存在，关闭默认反射序列化时该入口无法自行取得元数据。

## 为什么显式元数据就够了

[`Static/Messaging/MessageContractRegistry.cs`](Static/Messaging/MessageContractRegistry.cs) 独立实现新机制。`For<T>` 接收 `JsonTypeInfo<T>`，并在注册时捕获它，形成两个闭合泛型委托。运行时依然根据已登记的消息类型或合同名查字典，但执行 JSON 时直接调用这两个委托；不再要求序列化器从一个裸 `Type` 猜测对象结构。

```csharp
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProfileChanged))]
internal partial class MessageJsonContext : JsonSerializerContext;

var contract = MessageContract.For<ProfileChanged>(
    "profile.changed", MessageJsonContext.Default.ProfileChanged);
```

调用方只需在生成上下文中列出消息根类型。生成器会处理其可见的静态成员类型；`object` 成员或多态派生类型仍需显式声明所需类型。没有自动反射兜底。

## 哪些行为必须保留

旧版使用 `JsonSerializerDefaults.Web`，所以仓库调用方生成上下文仍用 Web 默认值：camelCase 名称、大小写不敏感读取、数字枚举及原来的空值规则保持一致。元数据现在由调用方提供，变更其序列化选项也是线协议变更，不能随意更换。

合同名和版本依然是允许列表，消息 Id 与 UTC 时间依然必须与信封一致。元数据仅替换 JSON 的成员发现方式，不会绕过这些校验。未知合同或版本继续明确失败。

## 验证边界

示例测试比较新旧 JSON 和跨版本读写结果，覆盖空值、Unicode、转义字符及整数边界；消息组件测试关闭默认反射序列化，执行包含嵌套对象和枚举的真实往返，并验证未知类型、版本、无效 JSON 和信封不一致。

这些检查证明自有注册表不再需要默认 JSON 反射。它们不代表 Wolverine、数据库驱动或整个宿主已通过 NativeAOT 发布验证，也没有将初始化方式的变化宣称为业务吞吐量提升。

源码消费探针 `build/consumers/MessagingConsumer.cs` 同样登记生成的消息契约，并使用同一契约读取真实 SQLite Outbox。该文件直接链接进 SelfManaged 测试，验证两个独立 DbContext 的提交、回滚和重复消费；这样组件 API 变更时会立即编译检查外部消费示例。本轮只运行源码消费测试，不运行打包或发布流程。
