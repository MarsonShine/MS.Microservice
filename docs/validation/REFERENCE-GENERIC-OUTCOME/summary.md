# Reference 泛型 Outcome 验收

日期：2026-11-17。基线 `4aa1b47e71344964120cda8f901392f5ec0e17dd`，验收对象为基线加本任务工作区改动。

Reference Application 使用内部 `Outcome<T>` 与通用 `ToEither`；订单与资料服务继续返回原公开 `Either`，保留各自空结果错误信息。更新资料成功测试覆盖返回值、事件发送和提交后清理；原有测试覆盖订单、资料错误和空结果。

- `dotnet test test/MS.Microservice.Reference.Application.Tests/MS.Microservice.Reference.Application.Tests.csproj -c Release --verbosity quiet`：11 通过，0 失败。
- `dotnet test test/MS.Microservice.Reference.Web.Tests/MS.Microservice.Reference.Web.Tests.csproj -c Release --verbosity quiet`：79 通过，0 失败。
- `git diff --check` 和 harness 检查：通过。

真实数据库及外部服务未在本次定向测试中验证。泛型成功类型须不与 `Error` 重叠；当前仅用于 `ProfileView` 和 `OrderView`。
