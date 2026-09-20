# AI 协议为什么使用生成的 JSON 元数据

## 聊天请求、响应和错误封包

旧实现已经复用 `JsonSerializerOptions`，所以这里不是“每次请求重新反射”的性能缺陷。问题是这些 options 并没有告诉序列化器 DTO 成员是什么；首次使用类型时仍需在运行时发现成员。NativeAOT 裁剪后不能假定这些成员和构造方式都存在。这一项属于 **AOT compatibility**，不承诺吞吐量提升。

聊天 DTO 是组件自己定义的固定协议，因此在编译时用 `[JsonSerializable]` 登记请求和响应即可。生成器会包含它们引用的消息、usage、schema 等成员类型。`JsonContent.Create`、普通响应、SSE 分片和错误解析全部传入生成的 `JsonTypeInfo<T>`，避免某个分支悄悄回到默认反射。

生成器不替我们决定协议。仍使用 `JsonSerializerDefaults.Web` 保留大小写不敏感读取和数字字符串读取，并保留 `JsonPropertyName` 的 snake_case 字段。请求继续忽略 null，`stream=false` 等非空默认值继续发送。两种错误封包和格式损坏时的回退保持原语义。

`Legacy/AI/ChatJson.cs` 保存原来的 options 泛型序列化机制；`Static/AI/ChatJson.cs` 独立展示具名 DTO、生成 context 和显式类型元数据调用。实际生产 context 放在协议所有者的内部，不作为额外公共 API。

验证在 AI.Core 测试进程关闭默认反射序列化，覆盖真实请求、普通响应、嵌套 schema、SSE usage、两类错误以及坏 JSON。它证明自有代码不依赖默认反射，不等同于完整应用已通过 NativeAOT 发布验证。
