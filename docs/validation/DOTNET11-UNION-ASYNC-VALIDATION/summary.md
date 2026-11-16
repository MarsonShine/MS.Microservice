# .NET 11 联合类型与异步验证验收

日期：2026-11-16。基线为 `936c6f1512a2100a0b60405dd04fb56799a9822b` 加此前未提交的 [.NET 11 升级](../DOTNET-11-UPGRADE/summary.md)及本次工作区改动；SDK 11.0.100，Windows。

## 范围与行为

- C# 语言版本由 14 升至 15。幂等存储新增 `IdempotencyLookupResult` 联合类型和 `FindResultAsync`：缺失、已保存响应、不同请求分别为类型分支。原 `FindAsync` 保留；未初始化联合结果拒绝转换。Reference Web 在首次查询与并发冲突后的重查中消费这些分支。
- Excel 与 CSV 报告导入分别新增 `ImportWithDataAnnotationsAsync` 和 `ImportCsvWithDataAnnotationsAsync`，显式启用 .NET 11 异步数据注解验证；原同步回调入口不变。仅验证成功转换的行，依赖服务由调用方管理，取消与异常传播，错误映射到原有行列报告。工作簿先解析后顺序验证，CSV 按行处理。
- 官方依据：[C# 联合类型](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)、[ASP.NET Core 异步验证](https://learn.microsoft.com/aspnet/core/fundamentals/validation?view=aspnetcore-11.0)。数据注解对象验证不递归；同一对象的规则可能并发，依赖服务须支持并发。

## 验证

- `dotnet restore --configfile nuget.config --verbosity quiet`：通过。
- `dotnet build --no-restore -c Release --verbosity quiet`：通过，0 警告、0 错误。
- `dotnet test --no-build -c Release -m:1 --logger 'console;verbosity=normal'`：48 个测试项目、3,572 通过、158 跳过、0 失败；退出码 0。本机输出在 `artifacts/validation/net11-union-async-tests.log`，该目录不纳入版本库。
- 幂等组件定向测试 12 通过；Reference Web 定向测试 78 通过；Excel 测试 149 通过、19 跳过。覆盖联合类型分支、原接口、取消、幂等重放，以及异步验证的 DI、取消、错误位置和流所有权。
- 根解决方案之外的 7 个独立基准项目分别以 `dotnet build <project> -c Release --configfile nuget.config --verbosity quiet` 构建，全部 0 警告、0 错误。
- 对当前构建结果打包 Excel、Core、Domain.Primitives、Threading 和 Idempotency.EFCore 后，外部临时消费者从本地包源还原并运行，通过 SQLite 测试三种 union 分支、旧接口和默认值，并测试 CSV/XLSX 异步注解公开入口。包来自未提交工作区构建，不能仅凭包内 revision 认作 HEAD 内容。
- `python "$HOME/.agents/harness/harness.py" check` 和 `git diff --check`：通过。

真实 PostgreSQL 并发、外部服务和容器环境未验证；这些限制延续此前的 .NET 11 升级验收。
