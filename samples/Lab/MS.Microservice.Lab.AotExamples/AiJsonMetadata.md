# AI 协议为什么使用生成的 JSON 元数据

## 聊天请求、响应和错误封包

旧实现已经复用 `JsonSerializerOptions`，所以这里不是“每次请求重新反射”的性能缺陷。问题是这些 options 并没有告诉序列化器 DTO 成员是什么；首次使用类型时仍需在运行时发现成员。NativeAOT 裁剪后不能假定这些成员和构造方式都存在。这一项属于 **AOT compatibility**，不承诺吞吐量提升。

聊天 DTO 是组件自己定义的固定协议，因此在编译时用 `[JsonSerializable]` 登记请求和响应即可。生成器会包含它们引用的消息、usage、schema 等成员类型。`JsonContent.Create`、普通响应、SSE 分片和错误解析全部传入生成的 `JsonTypeInfo<T>`，避免某个分支悄悄回到默认反射。

生成器不替我们决定协议。仍使用 `JsonSerializerDefaults.Web` 保留大小写不敏感读取和数字字符串读取，并保留 `JsonPropertyName` 的 snake_case 字段。请求继续忽略 null，`stream=false` 等非空默认值继续发送。两种错误封包和格式损坏时的回退保持原语义。

`Legacy/AI/ChatJson.cs` 保存原来的 options 泛型序列化机制；`Static/AI/ChatJson.cs` 独立展示具名 DTO、生成 context 和显式类型元数据调用。实际生产 context 放在协议所有者的内部，不作为额外公共 API。

验证在 AI.Core 测试进程关闭默认反射序列化，覆盖真实请求、普通响应、嵌套 schema、SSE usage、两类错误以及坏 JSON。它证明自有代码不依赖默认反射，不等同于完整应用已通过 NativeAOT 发布验证。

## 媒体请求为什么需要具名 DTO

原来的媒体 helper 接收 `object`，匿名对象再经 `object[]` 放进 Qwen 的嵌套消息。编译器知道匿名类型，但 helper 的公共约束已经丢失类型；序列化器只能在运行时检查实际对象。仅给最外层类型生成 metadata，无法替嵌套的未知 object 注册其实际类型。

现在 helper 要求 `T` 和 `JsonTypeInfo<T>` 成对传入。TTS、图片生成和 Qwen 请求分别有自己的具名协议 DTO。Qwen 的每个 content 项用两个可空字段 Image/Text 表达，继续省略 null，因此仍是先 `{image: ...}` 再 `{text: ...}`，不会多发 null 字段或类型判别字段。

这项改动不替换已有 multipart 处理和 JsonDocument 响应解析：它们本来没有 POCO 类型发现的问题。原有鉴权、重试、默认 count/format 和二进制响应也保持原语义。OpenAI、Qwen、DeepSeek 的完整测试进程均关闭默认反射序列化；协议断言包括 null 省略、数值 speed、false 布尔值和 Qwen 嵌套字段顺序。

`Legacy/AI/MediaJson.cs` 与 `Static/AI/MediaJson.cs` 使用同一 TTS 输入比较 JSON。具名 DTO 多了少量声明，但让序列化契约可被编译器完整枚举，并非因为匿名类型本身必然更慢。
