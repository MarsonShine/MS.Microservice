# 配置绑定的调用没改，为什么不再需要反射？

## 问题位于编译选项

原来的调用是 `Get<ExternalIdentityOptions>()`、`Get<string[]>()` 和 `AddOptions<TelemetryResourceOptions>().Bind(section)`。这些类型在编译时已经明确，但普通配置绑定器仍会在运行时发现属性并赋值。原调用文件保存在 [`Legacy/Configuration`](Legacy/Configuration) 中。

这是 **AOT compatibility** 问题。配置主要在启动或配置重新加载时绑定，没有证据表明它是业务热路径，因此不把这一步描述为显著的运行时性能优化。

## 为什么不手写一套赋值逻辑

SDK 自带配置绑定 Source Generator。分别在拥有调用点的 AspNetCore 和 Observability 项目启用：

```xml
<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
```

生成器识别具体类型的 `Get<T>` 和 `Bind` 调用，在编译阶段生成读取配置键、转换字符串、设置属性的代码，并用 interceptor 接管这些调用。业务源码保持熟悉的绑定 API；成员发现工作转移到了编译阶段。

这里已有明确类型，无需额外的包装层。反而把调用藏进一个任意 `T` 的通用辅助方法，会让生成器无法确定要生成哪些属性赋值。新增绑定入口也应保留具体类型。

[`Static/Configuration/GeneratedBinding.md`](Static/Configuration/GeneratedBinding.md) 说明如何查看实际生成代码。生成代码由 SDK 维护，不手动复制回生产代码。

## 保留哪些契约

数组仍按配置索引读取，缺失配置继续保留类和框架默认值；自定义声明名仍传给 JWT 配置，无效代理地址或布尔值继续失败。遥测的 `IOptionsMonitor` 仍订阅配置重新加载；这不额外承诺已创建的 OpenTelemetry resource 随之重建。

测试覆盖稀疏数组索引、缺失数组、非法地址、默认/自定义身份声明、缺失遥测段、布尔值大小写、非法布尔值及 options reload，并保留原有真实认证管道测试。

生成器只处理所在项目的调用点。启用这两个项目不会自动改变其他程序集中的配置绑定，也不代表认证中间件、遥测 SDK 或整个宿主已通过 NativeAOT 发布验证。
