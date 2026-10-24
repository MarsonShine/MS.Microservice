# Slice 08：通用身份与Reference策略解耦

Status: complete

范围：AspNetCore ExternalIdentity实现及兼容适配、ReferenceHost及必要宿主策略文件；AspNetCore身份测试、Reference.Web.Tests；两模块身份说明；状态/证据。范围外：Lab独立JWT/RBAC、业务权限含义、令牌签发、新配置模型。

根因：共享JWT注册强制携带Reference两项策略。修复引入真正通用的JWT入口，Reference迁移并拥有具体策略；旧公开3参入口/Options/Validate/HasPermission保留为兼容适配，不能仅删除既有行为或只新增无人使用的入口。

方案：同一ExternalIdentityOptions绑定/单例；新AddExternalJwtIdentity只验证JWT必需字段；通用RequireExternalScope显式要求认证、唯一非空subject和完整scope token，不能依赖命名策略继承DefaultPolicy。Reference保留原配置键/默认值/策略名并在启动阶段验证具体权限。

验收：AspNetCore身份相关源码重建测试、Reference.Web.Tests完整测试（含自定义权限及claim映射）、`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.AspNetCore') -ReportPath artifacts/validation/api-compatibility-slice08.json`。旧入口行为保持，新纯JWT不注册Reference策略，宿主实际授权不变。

结果：身份相关源码重建51/51通过（ExternalIdentityTests与ExternalJwtIdentityTests），Reference.Web.Tests完整79/79通过，均0失败0跳过、退出码0。AspNetCore及Core/Domain.Primitives/Threading共4包API基线比较通过，报告artifacts/validation/api-compatibility-slice08.json；其中未跟踪的新兼容源码也被当前包构建纳入。

身份命令：`dotnet test test/MS.Microservice.AspNetCore.Tests/MS.Microservice.AspNetCore.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~ExternalIdentityTests|FullyQualifiedName~ExternalJwtIdentityTests'`。
宿主命令：`dotnet test test/MS.Microservice.Reference.Web.Tests/MS.Microservice.Reference.Web.Tests.csproj --no-restore -c Release`。

证据覆盖：纯JWT空权限字段仍可注册且无两项Reference策略；JWT拒绝矩阵、唯一非空subject、完整/区分大小写scope；兼容入口完整校验及原mutable options闭包；真实Reference自定义权限/claim映射、actor对应档案及命名/default策略拒绝。此为新增通用入口与迁移保护，不把编译缺失或原本正确的兼容行为伪造为红测。

复核：原declaring type/签名、Options默认值与publicValidate/HasPermission保持；Reference使用通用入口及本地策略，只有兼容适配继续提供旧策略。无第二配置模型、Obsolete编译警告或Lab改动。git diff --check通过。

边界：保留旧适配是对外兼容义务，不是重新让新通用路径依赖Reference权限。此片未改变Slice06四入口包消费者的受测源码/依赖输入，复用其证据；未调用真实身份服务。
