# .NET 11 存量功能迁移验收

日期：2026-11-16。验收输入为基线 `936c6f1512a2100a0b60405dd04fb56799a9822b` 加此前未提交的 [.NET 11 升级](../DOTNET-11-UPGRADE/summary.md)、[联合类型与异步验证](../DOTNET11-UNION-ASYNC-VALIDATION/summary.md)和本次工作区改动；SDK 11.0.100，Windows。

## 范围

Reference 的 Profile 创建、修改和 Order 创建使用 C# 15 私有 union 表达现有事务结果，并在原公开方法边界转换回 `Either`。Lab 用户创建将既有异步角色目录读取和角色存在性检查迁入 `IAsyncValidatableObject`，由 `Validator.TryValidateObjectAsync` 执行；查询错误、规则异常和取消沿用原有处理。共享 Core `Either` 的修改试验因 .NET JSON 兼容性退化而放弃，没有修改其公开契约。实现步骤、兼容性取舍及官方来源见 [升级过程](../../NET11-Upgrade-Union-Async-Validation.md)。

## 证据

- `dotnet restore --configfile nuget.config --verbosity quiet`：通过。
- `dotnet build --no-restore -c Release --verbosity quiet`：通过，0 警告、0 错误。
- Reference 应用测试 10 通过；ReferenceHost 定向测试 78 通过；Lab 用户创建和既有命令扩展定向测试 15 通过。覆盖公开结果、HTTP 状态和位置、union 默认值、查询延迟、角色不存在、查询与规则异常边界、取消及失败不持久化。
- 首次 `dotnet test --no-build -c Release -m:1 --logger 'console;verbosity=normal'`：3,583 通过、158 跳过、1 失败；渲染取消测试清理 Chromium 临时文件时遇到 `UnauthorizedAccessException`，实际抛出同时包含取消与清理失败的聚合异常。单独重跑该测试通过。
- 同一完整命令第二次运行：48 个测试项目，3,584 通过、158 跳过、0 失败，退出码 0。原始日志分别保留在本机 `artifacts/validation/net11-existing-migration-tests.log` 和 `artifacts/validation/net11-existing-migration-tests-retry.log`；这些日志不纳入版本库。不能仅凭重跑通过确定首次清理失败的原因。
- `python "$HOME/.agents/harness/harness.py" check`、`git diff --check`：通过。

真实 PostgreSQL、MySQL、RabbitMQ 和容器运行未验证。仓库提供的两个工作单元不检查 `ExecuteAsync<T>` 的结果类型；外部自定义实现若按 `T` 特判，需要适配内部 union。更多边界见升级过程。
