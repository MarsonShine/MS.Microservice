# 仓库总体架构与阅读指南

MS.Microservice 同时承载三种用途：可复用组件、完整接入示例、教学实验。
理解这三种用途，比先记住每个项目名更重要。组件回答“提供什么能力”，
Reference 回答“这些能力怎样组合成一个服务”，Lab 回答“为什么需要这些设计，以及失败时会怎样”。

## 先选择你要做的事

| 需求 | 从哪里进入 | 不必先理解的部分 |
|---|---|---|
| 接入一个消息、日志、AI 或持久化组件 | 对应模块目录和独立解决方案 | Reference Web、Lab Host |
| 学习一个服务如何落地事务、身份、业务和迁移 | samples/Reference | Audio、Excel、AI 等可选模块 |
| 做对照、故障和原理实验 | samples/Lab、六个 Lab 文档 | 发布系统或 Kubernetes |
| 修改通用语言/领域辅助代码 | src/Core、src/Domain.Primitives | 具体用户、角色、订单模型 |

[根 README](../README.md)是入口地图，[快速开始](Getting-Started.md)负责启动命令，
[Lab 文档](labs/README.md)负责实验步骤。本文解释职责、依赖与取舍，不重复全部命令。

## 目录为什么按模块分组

AI、Excel、Logging、Messaging、Persistence、Idempotency 都有独立测试和自己的接入说明。
因此采用模块优先的组织方式：

    MS.Microservice.AI/
      README.md
      docs/
      src/
        MS.Microservice.AI.Abstractions/
        MS.Microservice.AI.Core/
        ...
      test/
        MS.Microservice.AI.Core.Tests/
        ...
      MS.Microservice.AI.slnx

其他模块按需要组合 README、src、test 和独立解决方案。根 src/test 保留较小独立组件、跨模块架构测试和宿主测试；
samples 按实际应用组织。根 MS.Microservice.slnx 用于全仓库开发，每个模块的 slnx 用于日常独立打开。

| 组织方式 | 更方便的事情 | 代价 |
|---|---|---|
| 全部平铺到根 src/test | 小型单应用导航、简单的统一目录约定 | 多项目模块的代码、测试和说明分散；只看一个模块也要跨目录找 |
| 模块目录内包含 docs/src/test/slnx | 按能力维护、学习、独立打开解决方案 | 构建工具需要识别多个源码根；必须明确跨模块依赖 |

本仓库以可复用模块和教学为目标，第二种更适合。这里的“模块”是能力边界，
不是把所有基础设施重新包进一个大 Infrastructure。目录不能代替依赖规则：
共享组件仍不得依赖 Reference、Lab 或具体业务模型。

模块解决方案中的 Dependencies 列出必要的跨模块项目。独立打开解决方案不等于完全没有依赖，
也不意味着可以忽略根 SDK/中央包配置后只复制一个文件夹。
需要脱离仓库接入时，使用[依赖闭包导出或包消费](components/consumption.md)。

## Core 与模块是什么关系

- Domain.Primitives 放通用实体、审计等小契约，不放 User、Order 等具体业务模型。
- Core 提供通用函数式结果、规格、集合、序列化、HTTP 等辅助能力，并复用 Domain.Primitives。
- 各模块按实际需要引用 Core，而不是被强制装入 Core。Messaging 的公共契约可以不依赖 Core；
  AI.Core 也不因为教育图像实验而依赖通用 Core。
- Core 不是“所有不好归类的代码”的收纳箱。业务规则、数据库映射、服务启动和第三方框架的专用配置各有归属。

Core → Domain.Primitives 是当前合理的依赖关系。类型的命名空间与程序集名称不一定完全一致，
部分通用类型保留历史命名空间；判断依赖应看项目引用和类型归属，不只看名称前缀。

## Reference 到底是什么

Reference 是参考应用，不是新的框架层，也不是使用组件前必须启动的中心服务。
它取代“在一个 Web 中混放正式业务与所有实验”的示范方式：
用一个实际可运行的用户档案场景，展示组件的生产接入边界。

它管理外部身份对应的本地档案、应用内角色与审计记录。
外部身份由 (issuer, subject) 标识，本地档案拥有自己的 Id。
Reference 验证外部 JWT/OIDC，不实现账号密码登录、签发令牌或默认管理员账号。

| Reference 项目 | 放什么 | 为什么单独存在 |
|---|---|---|
| Domain | 档案、业务角色、领域规则和领域事件 | 规则不需要 HTTP、EF 或消息框架才能测试 |
| Application | 用例、仓储端口、集成事件和业务 Handler | 组织业务动作并显式映射跨边界事件，不决定可靠消息实现 |
| Persistence | 业务 DbContext、EF 映射、仓储实现、两套迁移集 | 数据库结构属于应用，不能藏在通用持久化包中 |
| Web | HTTP、外部身份、权限、配置、健康检查和 Provider 选择 | 这是组合根，负责把具体组件接起来 |
| DatabaseMigrator | 导出 SQL、显式应用迁移、初始化消息拓扑 | Web 启动不隐式修改数据库结构，迁移可单独审查 |

五个项目让示范边界可见，并不是要求每个小应用都拆成五个程序集。
代价是初次打开会看到更多项目；收益是可以单测规则、替换消息实现、单独管理迁移。
如果只需要类库，完全可以不引用 Reference。若复制整个示例，则应根据自己的业务改造领域模型，
不要把示例中的 reader/editor/administrator 当作通用权限体系。

“生产参考”描述的是实现目标与接入方式，不代表已经完成你所在环境的发布认证。
真实依赖恢复、部署环境与发布验收仍须有各自的执行证据。

## Lab 为什么还存在

Lab 是独立教学 Host，保留本地登录签发、函数式对照、订单事件溯源和文件处理等实验。
演示 Controller 物理位于 Lab，Reference 不靠运行时过滤来隐藏它们。

Lab 的消息实验复用 Reference.Application / Persistence 中的示例业务类库和共享消息组件，
不复制第二套 Inbox/Outbox；两个 Web Host 互不引用。这样的复用有意让同一业务规则
出现在参考接入和教学场景里，代价是消息实验仍会看到几个 Reference 类库依赖。

教育图像是具体业务编排，位于 samples/AI；AI 模块保留通用模型调用与 QuestionGeneration Harness。
自研工具是否保留取决于用途，教学性实现不应被包装成所有服务都必须使用的基础设施。

## 可靠消息为什么这样拆

业务只使用 Messaging.Abstractions 的事件、发布器、工作单元、Handler 和失败操作接口。
应用必须在最外层 IUnitOfWork.ExecuteAsync 中调用 EnqueueAsync；后者暂存消息，
返回成功并不表示 Broker 已接收。

默认路径：

    业务用例 → 自研 EFCore 工作单元 / Outbox → RabbitMQ 确认
    RabbitMQ 投递 → Inbox 领取 → 业务 Handler → 业务与完成标记原子提交

替换路径：

    同一业务用例与 Handler → Wolverine 事务、可靠存储、发送与恢复

拆出这两个完整实现，是为了让替换不局限于一个 Send 方法。
Inbox 的租约表、Outbox 状态值是自研内部模型，不强迫 Wolverine 模拟它们。
同一宿主只能注册一个可靠机制，不能同时启用两套保存钩子和后台派发链。

几个容易误解的选择：

- 领域事件先描述进程内业务变化，需要跨边界时显式映射成集成事件，避免把所有领域对象自动广播。
- 消息类型使用名称和版本，避免把程序集限定名变成长期存储协议。
- 业务和 Outbox 使用同一个 DbContext 事务，否则“业务成功、消息没存下来”仍可能发生。
- 消费成功必须以持久化结果为准；发送确认丢失仍可能重投，所以幂等不可省略。
- 去重有保留期；永久性防重复要求由业务唯一约束保护。
- 两种具体 DbContext 分开模型与迁移，避免切换配置后混用模型；配置切换不会自动迁移在途消息或业务数据。

详细映射与阅读顺序见[消息模块设计](../MS.Microservice.Messaging/docs/design.md)。

## 注释和说明应该放在哪里

公共契约的 XML 注释说明行为、前提与限制；事务、取消、所有权等非直观逻辑附近说明原因。
模块 docs 解释跨文件设计，模块 README 给出接入与阅读入口，Lab 放实验步骤。
不在每一行重复代码含义，也不以“有很多注释”代替明确契约。

建议沿这些位置阅读：

| 问题 | 代码入口 |
|---|---|
| 为什么最外层提交、入队时复制消息 | SelfManagedUnitOfWork |
| 为什么租约续期不能并发使用业务 DbContext | LeaseGuard 与 SelfManagedReceiver |
| 为什么业务 Handler 不能再被原生框架发现一次 | WolverineMessagingExtensions |
| 为什么取消和重试共享逻辑，而流式解析独立 | AIHttpExecution、OpenAICompatibleChatProviderBase |
| 为什么审计接口分别判断、客户端按 scope 创建 | AuditTimestampExtensions、SqlSugarPersistenceServiceCollectionExtensions |
| 为什么日志不负责决定业务身份或暴露正文 | Logging 模块设计与 HTTP 日志中间件 |

## 验证和当前边界

根解决方案检查整体组合；模块解决方案检查模块及其依赖；架构测试检查共享代码不引用样例。
组件源码导出与仓库外包消费检查“离开原目录能不能接入”，与目录布局无关。

本地单元/SQLite/协议检查不能替代真实 PostgreSQL/RabbitMQ 故障运行。
Docker 执行和发布验收未完成的部分应明确标注，不能因测试被跳过而声称全部生产验证通过。
当前范围也不包含多实例部署、Saga、多租户或 Kubernetes 等扩展。
