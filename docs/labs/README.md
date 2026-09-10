# 一个 Lab，六个递进实验

这套实验使用 samples/Lab/MS.Microservice.Lab。
业务档案服务和 Handler 复用 Reference 类库，可靠消息复用共享组件；
本地账号、订单事件溯源和函数式对照仍保留在同一个 Lab 中。

先阅读[快速开始](../Getting-Started.md)，在第一个终端启动：

    ./build/start-local.ps1 -Lab

脚本完成一次基础演练后保持宿主运行。以下每个实验都在第二个 PowerShell 终端、
仓库根目录执行。先加载公共操作函数：

    . ./build/local-tools.ps1
    $settings = Get-LocalSettings
    $labUrl = 'http://127.0.0.1:5210'
    $headers = @{ Authorization = 'Bearer ' + (Get-LabToken $settings) }

不要打印 settings 或 headers。令牌过期得到 401 时，重新调用 Get-LabToken 获取令牌。默认实验数据库为 ms_lab_messages_selfmanaged。

| 实验 | 关注的不变量 | 可观察结果 |
|---|---|---|
| [1. 请求与分层](01-request-and-layers.md) | 验证失败不产生业务或消息 | 正常请求有档案和审计，非法请求没有副作用 |
| [2. 事务与迁移](02-transactions-and-migrations.md) | 最外层提交，迁移显式执行 | 回滚没有部分结果，SQL 与历史可核对 |
| [3. 自研 Inbox/Outbox](03-inbox-outbox.md) | 领取、租约、确认各有明确含义 | 可从数据库解释消息生命周期 |
| [4. 故障与恢复](04-failure-and-recovery.md) | 只恢复已提交工作，失败有界 | 中断、重启、毒消息和重放有证据 |
| [5. 诊断与权限](05-diagnostics-and-permissions.md) | 故障可定位，运维操作受保护 | Trace 可串联，普通账号得到 403 |
| [6. 替换消息组件](06-replacement.md) | 业务契约和 Handler 不变 | 同一用例在两种配置下运行 |

每次实验记录请求结果、业务 Id、消息 Id、数据库状态与日志证据。
没有观察到目标状态时先检查配置和依赖，不以固定等待时间认定实验成功。

独立源码/包接入见[组件消费配方](../components/consumption.md)。
SqlSugar、Audio、Excel、AI、订单事件溯源和函数式实现是专项对照材料，
不需要为它们额外创建完整宿主。

这套文档提供可执行步骤；本机已检查的范围与未执行的 Docker 场景见
[验证边界](../Getting-Started.md#验证边界)。发布验收另行安排。
