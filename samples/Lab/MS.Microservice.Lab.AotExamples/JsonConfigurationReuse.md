# JSON 配置复用：把固定准备工作移出每次调用

一次缓存命中已经拿到了 JSON 字节，旧代码却仍然创建 `JsonSerializerOptions`、创建 Unicode 编码器，再包一层 `MemoryStream`。HTTP JSON 读取也每次创建同样的配置。这里重复的是准备工作，业务数据本身仍需逐次解析。

## 为什么配置可以共享

`JsonSerializerOptions` 除了保存大小写、命名和编码规则，还维护类型元数据缓存。配置相同时复用同一实例，后续调用就可以继续使用已准备好的元数据。[官方说明](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/configure-options)明确支持跨线程复用，并说明首次序列化或反序列化后配置不可再修改。

新实现把各辅助类的配置放在私有 `static readonly` 字段中，初始化后不再修改：

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = false,
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
};
```

编码器随配置只创建一次。每次调用仍解析自己的输入、生成自己的结果，不会缓存业务对象，也不需要为序列化调用增加锁。不要为了设置某个请求的选项去修改共享配置。

## 为什么没有统一成全局默认配置

| 规则 | 缓存辅助方法 | HTTP JSON 读取 |
|---|---|---|
| 属性名匹配 | 区分大小写 | 不区分大小写 |
| 数字字符串，如 `"Count":"3"` | 拒绝 | 拒绝，外层返回默认值 |
| 无效 JSON | 抛出 `JsonException` | 保留原有捕获并返回默认值的行为 |

缓存写入还保持原属性名、null 字段和原有 Unicode 转义规则。借用仓库的 camelCase 默认配置会改变缓存格式，改用 `JsonSerializerOptions.Web` 又会允许从字符串读取数字。两份小的固定配置比合并后改变契约更合适。

## 为什么缓存读取不再包装成流

缓存后端返回的是完整 `byte[]`，可以直接把只读 Span 交给 `JsonSerializer.Deserialize`。这省掉了 `MemoryStream` 和流式读取的准备工作；后端获取数据仍然是异步操作。HTTP 输入继续使用原来的流式异步解析，未额外读取成字节数组。

更换解析入口时还必须保留两个行为：

- 流解析原本接受开头的 UTF-8 BOM，而直接解析 Span 需要先移除这个前缀。新实现只调整 Span 起点，不复制、不修改缓存字节；空正文、错位或重复 BOM 仍按原规则失败。
- 直接解析字节没有 `CancellationToken` 参数，因此命中后在解析前后显式检查取消。内存解析是同步 CPU 工作，不承诺能在解析中途立即中断。

## 如何证明收益和兼容性

[生产路径测试](../../../test/MS.Microservice.Core.Tests/Serialization/JsonReuseRegressionTests.cs)检查实际分配量，并覆盖大小写、中文与转义、null、枚举、嵌套对象、失效 JSON、过期参数、取消、BOM 和并发调用。测试不依赖执行耗时，也不要求某个精确分配字节数。

在 .NET 10.0.12、Debug、小型 DTO、内存后端下，预热 32 次后测量 64 次调用，得到以下单次观测：

| 路径 | 原实现，字节/次 | 复用后，字节/次 |
|---|---:|---:|
| 缓存读取 | 45,809 | 400 |
| 缓存写入 | 49,825 | 576 |
| HTTP JSON 读取 | 48,043 | 2,576 |

这些数字说明这组输入下重复初始化的成本，不代表真实网络请求的吞吐量或所有数据模型的收益。

`Legacy/JsonConfigurationExample.cs` 和 `Static/JsonConfigurationExample.cs` 分别独立保留旧、新 JSON 读写步骤；[对照测试](../../../test/MS.Microservice.Lab.AotExamples.Tests/JsonConfigurationExampleTests.cs)检查字节输出和解析行为。示例只展示 JSON 部分，缓存后端和 HTTP 外层错误策略仍由生产辅助方法负责。

本主题改变配置生命周期，尚未消除默认 JSON 元数据发现对反射的依赖。NativeAOT 所需的生成元数据或显式 `JsonTypeInfo` 属于后续接口迁移。
