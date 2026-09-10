# 快速开始

需要 global.json 指定的 .NET SDK、PowerShell 7、Git，以及支持 Compose v2 的 Docker。
AI 账号不是前提。所有命令从仓库根目录执行。

## 先准备配置

    ./build/start-local.ps1 -PrepareOnly

只生成 artifacts/local/settings.json 和 .env，并检查 Realm JSON，不运行 Docker。
重复执行保留既有凭据；文件位于 Git 忽略目录，不要提交或打印其中内容。

## 启动参考服务

    ./build/start-local.ps1

脚本启动独立 PostgreSQL、RabbitMQ 和 Keycloak，显式应用默认 SelfManaged 迁移及 Broker 拓扑，
随后启动 Reference。它检查匿名挑战、普通身份拒绝、业务提交与审计消费，再暂停专用 Broker，
验证业务仍能保存，恢复 Broker 后等待对应审计效果。

服务位于 http://127.0.0.1:5200；脚本保持运行，Ctrl+C 停止该宿主，依赖数据卷保留。
加入 -Smoke 可在演练完成后自动结束宿主，它是本地演练，不表示发布验收已完成。
日志在 artifacts/local/Reference.stdout.log 和 Reference.stderr.log。

## 启动一个教学 Lab

    ./build/start-local.ps1 -Lab

Lab 位于 http://127.0.0.1:5210，仍使用同一套可靠消息组件。
脚本显式初始化 ms_lab_activation / ms_lab_event_store，并通过 --seed-lab-users 创建
lab-operator 和 lab-reader；密码来自生成的本地配置，数据库只存哈希。
已有账号的密码和权限不被覆盖。随后运行与参考服务相同的业务与 Broker 恢复演练。

开始学习：[六个递进实验](labs/README.md)。

## 选择 Wolverine

    ./build/start-local.ps1 -Lab -Provider Wolverine

默认始终为 SelfManaged。教学模式为两种实现使用独立业务库和队列前缀；
切换不会搬迁业务数据、待发消息或死信。运行前先结束旧宿主，
并按照[实验六](labs/06-replacement.md)检查旧消息状态。

## 依赖与数据

| 资源 | 本机地址或数据库 |
|---|---|
| PostgreSQL | localhost:55432 |
| RabbitMQ AMQP / 管理页面 | localhost:55672 / http://localhost:55673 |
| Keycloak | http://localhost:58080 |
| Reference 业务库 | ms_reference_selfmanaged / ms_reference_wolverine |
| Lab 消息实验库 | ms_lab_messages_selfmanaged / ms_lab_messages_wolverine |
| Lab 本地身份、订单实验 | ms_lab_activation / ms_lab_event_store |

原有服务器上的 ms_activation 等练习数据库不在这些初始化目标中。
只有以下显式命令会删除本套开发环境的数据卷：

    ./build/reset-local.ps1 -ResetDedicatedVolumes

先结束运行中的宿主再重置。该命令只针对 ms-reference-lab Compose 项目。
如果仅需停止依赖并保留数据，在另一个终端执行：

    . ./build/local-tools.ps1
    Invoke-LocalCompose stop

Keycloak 使用[官方开发容器](https://www.keycloak.org/getting-started/getting-started-docker)，
通过[Realm 环境变量占位符](https://www.keycloak.org/server/importExport)注入客户端 Secret。
Reference 验证标准 JWT/OIDC；本地密码签发仅存在于 Lab。

## 验证边界

准备模式、脚本语法、数据库配置隔离、Lab 账号初始化和权限逻辑已有本地检查。
受 Docker 执行限制，完整 Compose 启动与 Broker 恢复命令未在本机执行；
发布验收不包含在这套文档的完成声明中。
