# 查询参数：把字段发现改成字段声明

旧实现根据传入对象的运行时类型枚举属性，然后缓存 IL 或表达式访问器。缓存已经避免每次请求都重复反射；这里要解决的是 NativeAOT 不支持运行时生成 IL，以及裁剪无法从任意 object 推断所需属性。

现在用 `QueryParameterMap<T>` 明确声明字段及顺序，委托由 C# 编译器编译：

```csharp
private static readonly QueryParameterMap<Search> SearchMap = new(
    ("name", static value => value.Name),
    ("ids", static value => value.Ids));

// client 是构造时已登记 Result JSON 元数据的 LogHttpClient。
await client.GetAsync<Result, Search>("/search", request, SearchMap);
```

映射只保存读取方法，不保存查询值；同一个映射可以复用，每次仍读取当前对象。未声明的属性不会被访问，也不会为了索引器、私有属性或计算属性去扫描元数据。字段重命名和顺序都由声明决定。

原有字典入口继续支持字典。对象入口需要增加映射参数，不再自动回退反射。值处理仍由同一套格式化函数负责：null 跳过、字符串作为单值、集合展开、日期往返格式、不变文化数字及 URI 转义。

`Legacy/Query` 保存完整旧 HTTP 辅助源码快照（`.cs.txt`），以及可运行的“发现属性后缓存表达式委托”示例；`Static/Query` 独立实现显式委托方式。两者共享示例值格式化，便于观察变化只发生在字段来源。

表达式在 NativeAOT 下可能解释执行，并非所有 Compile 调用都必然失败；生产旧路径默认使用的 Reflection.Emit 才是明确不受支持的运行时生成代码。仅切换解释器也没有解决任意 DTO 的裁剪契约。

测试比较新旧输出，同时覆盖显式字段顺序、未选择 getter 不执行、映射定义不受外部数组修改影响、值更新、空集合、嵌套集合、枚举和文化格式。这里不把单元测试通过解释为整个依赖图已经通过 AOT 发布验证。

直接扩展 HttpClient 时，模型响应入口还需传 `JsonTypeInfo<Result>`：`http.GetAsync<Result, Search>("/search", request, SearchMap, json.Result)`；返回原始 HttpResponseMessage 的入口不要求 JSON 元数据。详见 [Core JSON](CoreJsonMetadata.md)。
