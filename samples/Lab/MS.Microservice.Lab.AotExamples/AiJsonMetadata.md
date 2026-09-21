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

## 扩展题型：为什么 Type 注册还不够

题型由宿主扩展，组件无法在编译期列出所有候选类。旧 contract 用 `Type` 加 `DefaultJsonTypeInfoResolver` 自动发现属性，并用非泛型 `JsonStringEnumConverter` 动态构造枚举转换器；Harness 还另建了一套 Web options 比较候选。前者依赖裁剪后的成员，后者可能与发送/接收使用不同的字段或枚举规则。

现在宿主用生成 context 的 `JsonTypeInfo<T>` 注册候选类型。这个对象包含构造、读写和嵌套成员的静态元数据，`Type` 只作为字典 key，不用于发现属性。找不到注册项时明确失败。schema、序列化、反序列化、修复范围比较和无进展判断都通过同一个 contract；schema 仍在首次需要时从这些已生成的元数据导出并缓存。

宿主 context 应使用 `SystemTextJsonQuestionContract.CreateOptions()`；宿主枚举再显式添加 `JsonStringEnumConverter<MyEnum>(JsonNamingPolicy.CamelCase, false)`。不能直接使用 `UseStringEnumConverter = true` 冒充原先严格规则，因为还要拒绝整数枚举值。组件注册时拒绝宽松数字、忽略大小写、注释、尾随逗号和允许未知字段的 options。自定义 converter 和属性特性仍由宿主负责，它们不能依赖反射。

Draft/Review/Repair 使用具名 envelope。候选先按自身注册的元数据转为 `JsonElement`，再放入 envelope，因而不需要让生成器猜测抽象基类里的实际派生类型，也不需要增加 `$type` 字段。修复前后对象用同一元数据生成 JSON，确保 allowlist 判断对应真实 JSON 字段。

`Legacy/AI/QuestionJson.cs` 保留原 contract 实现；`Static/AI/QuestionJson.cs` 独立提取显式注册和派生类型分派的核心。生产测试关闭默认反射，覆盖嵌套类型、枚举/schema、一致的 envelope、非法 JSON、未知类型、修复越界和无进展。扩展点变更要求宿主在 `AddDefinition<T>()` 旁调用 `AddJsonTypeInfo(context.MyCandidate)`；它不自动发现程序集。

关闭默认反射还暴露了一个容易遗漏的重载：`JsonArray.Add(propertyName)` 选择泛型 `Add<string>`，仍会请求默认 string 元数据。schema 的 required 列表现在用 `JsonValue.Create(propertyName)` 创建内置字符串节点，再添加节点；此处应选择直接的 DOM 操作，而非为它开启反射回退。

## 静态分析补充

即使 `JsonValue.Create(string)` 已选用标量重载，`JsonArray.Add(JsonValue)` 仍可能优先绑定泛型 `Add<T>`。使用 `Add((JsonNode?)JsonValue.Create(name))` 明确选择节点重载，避免其带运行时序列化要求的泛型入口。真实 ILLink 分析器验证该调用不再报 IL2026/IL3050；原严格 schema 测试继续验证 required 内容。

DI 的泛型入口还需要声明构造函数的保留需求：验证器和题目定义保留公开构造函数，Options 保留公开无参构造函数。`DynamicallyAccessedMembers` 让裁剪器保留框架创建对象时需要的成员，不会生成代码，也不表示 DI 内部已经消除了构造函数发现。标记为什么要沿泛型参数传递，以及它与 Source Generator 的区别，见 [DynamicallyAccessedMembers 入门](DynamicallyAccessedMembers.md)。
