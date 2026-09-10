# 本地开发依赖

由 ../start-local.ps1 管理，Compose 项目名固定为 ms-reference-lab，端口仅绑定回环地址。

- start-local.ps1 -PrepareOnly：只准备被 Git 忽略的本地凭据，不启动依赖。
- start-local.ps1：Reference + Keycloak，默认 SelfManaged。
- start-local.ps1 -Lab：同一个教学 Host、本地签发与独立消息实验数据库。
- -Provider Wolverine：在独立数据库和队列前缀运行替换实现。
- -ConsoleTelemetry：显式开启控制台遥测。
- -Smoke：本地演练结束后退出宿主；不是发布验收。
- reset-local.ps1 -ResetDedicatedVolumes：显式删除本 Compose 项目的开发数据卷。

已有账号密码、服务凭据和数据默认保留。Lab 初始化账号仅由迁移命令
--seed-lab-users 显式创建，且数据库名必须以 ms_lab_ 开头。
Reference 不创建本地登录账号，也不在 Web 启动时执行 DDL。

本机只检查了准备模式、配置隔离与脚本语法；Docker 操作未执行。
