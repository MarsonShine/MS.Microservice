# 性能与 AOT 写法对照

独立学习类库，不打包、不启动 Web，也不被生产组件引用。
`Legacy` 保留旧机制，`Static` 独立实现替代写法。按主题添加示例。

## 验证洗牌

| 实现 | 行为与成本 |
|---|---|
| `Legacy/ValidatedShuffleExample.cs` | 来自 `7b1c78a`。每轮丢弃三个洗牌副本；修复固定位置时随机寻找候选，全部相同的输入会无限循环。 |
| `Static/ValidatedShuffleExample.cs` | 使用官方 `Random.Shared.Shuffle(CollectionsMarshal.AsSpan(list))` 原地洗牌；轮换多个固定位置，单个固定位置有限扫描候选。最多十轮随机尝试，修复过程最多两次线性扫描。 |

生产入口仍为 `list.ValidatedShuffle()`，调用方无需迁移。
至少两个元素且值互不相等时保证全部错位；重复值尽力错位，始终保留元素及其数量并有限结束。
这里不保证随机排列的均匀分布。公开 `Shuffle` 仍返回新列表，不修改输入。
`AsSpan` 不复制列表；使用期间不增删元素。普通随机洗牌允许元素留在原位，因此仍需错位修复。

```csharp
var values = new List<int> { 1, 2, 3, 4 };
MS.Microservice.Lab.AotExamples.Static.ValidatedShuffleExample.ValidatedShuffle(values);
```

运行示例对照：

```powershell
dotnet test test/MS.Microservice.Lab.AotExamples.Tests
```

测试用带比较次数上限的值对象复现旧缺陷；不要直接用普通全相同列表运行旧实现。
随机输出按元素多重集合和错位契约比较，不比较具体排列。

该主题属于运行时性能修复，旧算法本身没有反射或动态代码生成问题。
示例的单元测试不构成 NativeAOT 发布验证。

## 集合转换

| 实现 | 行为与成本 |
|---|---|
| `Legacy/ArrayConversionExample.cs` | 先复制源数组取得长度，再枚举原输入转换。延迟计算执行两次，一次性序列失败，还分配完整的中间源数组。 |
| `Static/ArrayConversionExample.cs` | 使用官方 `Select(conveter).ToArray()`，一次读取、一项一次转换，直接物化结果。 |

生产入口仍为 `source.ToArray(conveter)`，无需迁移；参数名和空参数验证保持不变。
新实现边读取边转换。任一阶段失败即停止并释放枚举器，之前已经完成的转换不回滚。
对照测试检查相同结果、实际枚举/读取次数及一次性序列；该优化属于运行时性能，不涉及 AOT 兼容性。

## HTTP 正文日志

实现为何能简化、标准库承担了哪些职责，以及测试依据，见[HTTP 正文日志：为什么能移除自定义包装器](HttpBodyLogging.md)。

| 实现 | 行为与成本 |
|---|---|
| `Legacy/LoggingHttpClientHandler.cs` | 日志格式化时同步等待正文 IO；即使日志关闭，包装器也会先完整缓冲正文再转发。 |
| `Static/LoggingHttpClientHandler.cs` | 请求开始时检查日志级别；关闭时透传，开启时异步读取完整正文后记录字符串，由 `HttpContent` 缓存供后续发送和消费。 |

生产处理器的注入方式不变；`LazyContentLogger`、`LoggableHttpContent` 两个旧嵌套类型移至 `Legacy`，直接使用者改用 `HttpContent` 的异步读取/复制 API。
正文按声明字符集解码；读取失败保留错误标记，调用方取消则传播取消。响应尚未交付便发生取消或日志提供器异常时，处理器释放响应；成功交付后的响应及请求由调用方释放。

开启完整正文日志仍需缓冲整段正文；此模式不保证首块立即交付。
测试用受控分块内容检查首块转发和异步返回，用超时仅防止旧实现阻塞测试，不比较执行耗时。保留的旧接口另有对照用例。
本主题是阻塞和分配修复，不构成 AOT 发布验证。

## JSON 配置复用

旧、新实现见 `Legacy/JsonConfigurationExample.cs` 与 `Static/JsonConfigurationExample.cs`。
[教学说明](JsonConfigurationReuse.md)解释配置为什么可以共享、缓存与 HTTP 的规则为什么不能合并，以及直接解析字节时如何保留取消和 BOM 行为，并给出实际分配对照。
生产入口保持不变；本主题优化运行时分配，尚未迁移为 AOT 静态 JSON 元数据。

## 静音工作缓冲复用

旧、新写法分别位于 `Legacy/SilenceWritingExample.cs` 与 `Static/SilenceWritingExample.cs`。
[教学说明](SilenceBufferReuse.md)解释为什么长静音无需整段零数组、为什么共享私有零块而不共享流对象，以及如何保持尾块、时长、插入位置和编码器输入不变。

## 查询参数静态映射

[教学说明](QueryParameterMappings.md)解释缓存访问器与 AOT 兼容性的区别，以及如何用 `QueryParameterMap<T>` 保留查询协议并移除自动类型发现。旧源码与运行时访问器示例位于 `Legacy/Query`，独立静态实现位于 `Static/Query`。
