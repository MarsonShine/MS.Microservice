# SqlSugar JSON：显式登记列数据的序列化合同

旧 `SqlSugarSerializeService` 只拿到 `JsonSerializerOptions`，因此 `object` 写入和泛型读取都需要序列化器在运行时发现属性。`ObjectJsonConverter` 又自行创建了静态默认服务，调用方无法为它提供生成的元数据。完整原实现保存在 `Legacy/Persistence/*.cs.txt`。

新服务接收 `JsonTypeInfo[]`。写入时用运行时类型查找**已登记**的元数据，再调用 metadata 重载；读取时按 `T` 查找。`GetType()` 在这里只是字典键，不扫描成员、不生成代码。找不到合同立即抛 `NotSupportedException`，不会退回默认反射解析器。嵌套 `object` 的实际类型也必须出现在相同生成上下文中。

```csharp
var service = new SqlSugarSerializeService(
    StorageJsonContext.Default.OrderData,
    StorageJsonContext.Default.String);
var converter = new ObjectJsonConverter(service);
```

`ObjectJsonConverter` 的最后一步仍是创建同样的 `SugarParameter`：参数名为 `@索引`，空参数保留 null，数据库 `DBNull` 返回 `default(T)`。字符串仍序列化为带引号的 JSON 字符串；旧实现虽有一个字符串分支，但随后无条件序列化覆盖了它，因此不能误改成原文透传。需要 SqlSugar 特性通过无参构造激活 converter 的调用方，可声明一个无参子类，在基类构造中传入固定的 service；第三方 ORM 的类型激活机制不在本次 AOT 结论内。

Lab 使用专用生成上下文，保留 camelCase、忽略属性名大小写和中文编码设置，登记示范实体、实体列表、参数字典及其标量值。自定义 converter 放在创建生成上下文时使用的 options 上；注册对应 `JsonTypeInfo` 后，两种 SqlSugar 写入入口和读取入口使用同一合同。

验证在关闭默认反射序列化的测试项目中执行真实往返，覆盖自定义枚举转换、null、DBNull、字符串、错误 JSON、未知根类型和未知嵌套类型；Lab 测试检查参数字典与中文输出。`Legacy/Persistence/JsonColumns.cs` 和 `Static/Persistence/JsonColumns.cs` 用相同输入独立展示两种机制。此项是自有 JSON 边界的 **AOT compatibility** 改造，不代表 SqlSugar ORM 已支持 NativeAOT 发布。
