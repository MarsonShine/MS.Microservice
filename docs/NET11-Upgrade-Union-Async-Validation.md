# 从 .NET 10 升级到 .NET 11：让现有流程使用新特性

本记录面向维护 Reference 和 Lab 的开发者，说明仓库如何升级运行环境，以及现有结果处理和角色校验如何迁移。示例使用本仓库的业务路径；它们不是要求所有 `Either` 或所有同步校验都改写的通用规则。

## 先让同一套代码在 .NET 11 上运行

原工作区使用 SDK 10.0.401、`net10.0` 和 C# 14。升级时，[global.json](../global.json) 改为 SDK 11.0.100，[Directory.Build.props](../Directory.Build.props) 改为 `net11.0` 与 C# 15；独立基准项目、Docker SDK 和运行时镜像也同步。CI 从 `global.json` 读取 SDK。EF Core 和 PostgreSQL 提供程序仍使用 10.x：升级运行时不要求不兼容的数据库提供程序同时跨主版本。

还原与构建暴露了几个不能靠修改目标框架解决的问题：

- .NET 11 共享框架已提供部分 `Microsoft.Extensions` 程序集，受影响项目移除多余直接引用。SqlSugar 传递引入的 MySqlConnector 存在已公开漏洞，因此中央依赖固定到修复版本 2.6.1；真实 MySQL 运行仍需单独验证。
- Reference Web 的验证生成器与 Core 公开的 `Microsoft.System` 命名空间冲突。Web 通过程序集别名消除编译歧义，保留 `AddValidation()`；其他同样引用 Core 并使用该生成器的宿主也可能需要处理。
- .NET 11 改变 `double` 到 `decimal` 的转换。Excel 单元格和公式结果原先按约 15 位十进制有效数字解释；导入共享转换层恢复该语义，避免 `99.99` 出现二进制尾数。数据本身是浮点值，因此这里不承诺任意 decimal 无损往返。详见 [微软的转换兼容性说明](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions)。
- .NET 11 下后台服务未处理的异常在默认停止宿主行为下会传递给 `RunAsync`；部署时需要检查退出码和重启策略。详见 [微软的宿主行为变更](https://learn.microsoft.com/dotnet/core/compatibility/extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure)。

## 用 union 改造 Reference 的现有结果流

C# 15 的 union 将一组互斥的类型作为结果分支；`switch` 可以按类型处理。语言与默认值规则见 [微软联合类型文档](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)。

此前 [ProfileService](../samples/Reference/MS.Microservice.Reference.Application/ProfileService.cs) 在工作单元中直接返回 `Either<Error, ProfileView>`：查到重复身份返回 Left，保存成功返回 Right。现在工作单元使用私有 `Outcome(Error, ProfileView)`，业务分支直接返回 `Error` 或 `ProfileView`，服务在原有公开方法边界转换回 `Either`。变更资料的未找到和版本冲突，以及 [OrderService](../samples/Reference/MS.Microservice.Reference.Application/OrderService.cs) 的创建路径采用相同方式。未初始化的 union 没有有效 case，转换时显式抛出异常，不把它当成成功或未找到。

核心变化是事务里的结果类型，公开方法仍在最后转换：

```csharp
private union Outcome(Error, ProfileView);

var result = await unit.ExecuteAsync<Outcome>(async token =>
{
    if (await profiles.FindAsync(profile.Issuer, profile.Subject, token) is not null)
        return Error.Conflict("A profile already exists for this external identity.");
    profiles.Add(profile);
    await EnqueueChangesAsync(profile, actor, token);
    return ProfileView.From(profile);
}, cancellationToken);
return ToEither(result); // 省略成功后的领域事件清理
```

事务与消息行为仍由工作单元控制：两个提供程序都把结果当作普通返回值，异常、取消或已标记回滚的嵌套失败会阻止提交；只有成功分支清除领域事件。外部服务签名与 HTTP 响应没有改变。自定义工作单元如果按 `ExecuteAsync<T>` 的具体 `T` 特判，需要适配新的内部结果类型；仓库内两个实现不这样做。幂等存储的 [类型化查询](../MS.Microservice.Idempotency/README.md)同样在 Reference Web 的首次查询和竞争后重查中使用 union，旧 `FindAsync` 仍可供现有调用方使用。

为什么不直接给共享 `Either<L,R>` 加 `[Union]`？它被多个模块公开使用，有 `Match`、相等性与既有默认值语义。本机试验中，`JsonSerializerOptions.IgnoreReadOnlyProperties = true` 时旧 `Either<int, int>` 输出 `{}`；为它添加自定义 `[Union]` 后，相同序列化调用抛出 `InvalidOperationException`。Reference 内部结果先迁移，保留公开边界，避免把这种变化带给未知消费者；这不表示共享类型永远不能迁移，而是需要单独定义和验证它的序列化契约。

## 把已有角色校验迁入异步验证

[UserCreateAppService](../samples/Lab/MS.Microservice.Lab/Application/Users/UserCreateAppService.cs) 原先先完成命令校验，然后异步读取角色目录，再用同步规则检查请求中的角色。现在这一步由 `UserRoleCatalogValidation : IAsyncValidatableObject` 完成：`ValidateAsync` 等待原有 `GetAllRolesAsync`，再复用原角色存在性规则；服务调用 `Validator.TryValidateObjectAsync` 并将结果映射回原有 `Either`。

```text
当前用户 → 命令校验 → 异步读取角色并验证 → 映射用户 → 密码处理 → 持久化
```

异步路径的关键调用如下；同步 `Validate` 则拒绝调用：

```csharp
var valid = await Validator.TryValidateObjectAsync(validation,
    new ValidationContext(validation), validationResults: null, cancellationToken);
```

角色不存在仍返回原来的结构化验证错误；目录查询失败仍返回 `user.roles`，规则自身异常继续传播，失败时不创建用户。取消令牌传给角色查询；查询异常沿用原 `TryAsync` 的错误映射。同步 `Validate` 明确拒绝调用，因为目录读取必须异步进行。这个对象在应用层显式调用，不依赖 MVC 自动执行异步数据注解；MVC 的现有请求和状态码没有因此改变。异步与同步验证入口不会自动互相调用，规则应各自覆盖所需行为，见 [微软异步验证说明](https://learn.microsoft.com/aspnet/core/fundamentals/validation?view=aspnetcore-11.0)。

Excel/CSV 的既有报告导入仍支持同步业务回调；如业务规则确实需要异步服务，可显式使用 [异步数据注解导入入口](../src/MS.Microservice.Excel/README.md#导入与错误报告)。它只验证转换成功的行，不递归对象图；规则可能并发访问同一行的依赖服务，调用方须管理作用域和并发安全。

## 检查升级结果

项目级命令仍以 [README](../README.md#构建) 和 CI 为准。验证需要覆盖旧行为与新路径：Reference 应用及 HTTP 测试检查成功、验证失败、冲突、未找到和默认结果；Lab 测试检查异步查询、目录错误、取消与失败不持久化。全量测试和本机环境限制记录在 [本次验收](validation/DOTNET11-EXISTING-MIGRATION/summary.md)，此前版本升级的原始证据保留在 [.NET 11 升级验收](validation/DOTNET-11-UPGRADE/summary.md)。真实 PostgreSQL、MySQL、RabbitMQ 与容器验证需要相应环境，不能由内存或 SQLite 测试推断通过。
