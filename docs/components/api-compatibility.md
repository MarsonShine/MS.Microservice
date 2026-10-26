# 公共 API 兼容性检查

可复用组件的公开 API 与固定提交生成的 NuGet 包比较。基线配置位于 `build/api-baseline.json`，记录完整 Git SHA 和入口模块；检查还包括基线及当前入口的项目依赖闭包。代码删除公开成员、改变签名或收窄可见性时，SDK 的 ApiCompat 检查会失败。兼容地增加成员允许通过，参数改名检查也已启用。

## 执行

在仓库根目录使用 PowerShell：

```powershell
./build/validate-api-compatibility.ps1
./build/validate-api-compatibility-self-test.ps1

# 只检查所列入口及其依赖闭包
./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.Core', 'MS.Microservice.AI.Core')
```

默认报告为 `artifacts/validation/api-compatibility.json`，自检报告为 `artifacts/validation/api-compatibility-self-test.json`；两个脚本均可用 `-ReportPath` 指定其他输出位置。主报告记录 SDK、固定基线、当前 HEAD、工作区修改状态、包 hash 和各包结果；自检报告记录各个临时契约用例及其退出码。生成的包与源码快照保留在报告列出的临时目录，便于失败诊断，不应提交。

基线通过 `git archive` 解出，构建过程不会修改当前源码或创建工作树、分支。检查需要本地存在记录的提交，CI 应获取完整历史。每次检查均使用新的构建产物目录，包含独立的 `obj` 和 ApiCompat 验证标记，避免沿用上一次比较结果。

当前包直接从工作区构建，SDK 正常纳入新增编译文件，因此不要求修改已提交或新 `.cs` 已暂存。报告中的 HEAD 只是工作区的起点，不表示包完全来自该提交。检查期间应保持所选源码稳定；新增文件仍需正常审查。

`validate-module-consumption.ps1` 则继续从受控的 tracked 文件快照验证源码导出和包消费者。新增源码应明确暂存后再运行该检查，不能将忽略新文件的旧包验证作为新接口的验收。API 比较与实际包调用互补，前者不证明运行行为兼容；消费者说明见 [模块消费](consumption.md)。

## 基线与例外

基线不是动态分支或“当前 HEAD”。脚本不会自动刷新 SHA，也不会生成兼容性豁免；不要用 `NoWarn` 或 `CompatibilitySuppressions.xml` 隐藏未经审查的破坏。基线提交不可用、受保护项目缺失或基线包无法生成时，应修正输入或环境，不能跳过比较后宣称通过。

有意破坏 API 时，先明确受影响的调用方、迁移方式和版本策略，再单独审查基线变更。推进基线会改变后续兼容承诺，不能仅为消除失败而更新。新组件没有旧版本可比较，应在首次接受其 API 的提交后显式纳入基线；更改依赖闭包不会自动取消旧依赖包的保护。

自检在独立的小型项目中验证：相同 API 和新增成员通过，删除公开方法必须非零退出并报告 `CP0002`。还原、编译或环境失败不能算作删除成员已被正确识别。实现使用 SDK 自带的 [package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview)，不增加工具或运行时包。
