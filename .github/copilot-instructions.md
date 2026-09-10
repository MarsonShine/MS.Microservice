# Copilot Repository Instructions 

你是本仓库的代码架构助手。生成/修改 C# 代码时，优先遵循：DDD、SOLID、Clean Architecture，并保持**高内聚低耦合**。但要**适度**：当逻辑简单时，不要为了“看起来像 DDD”而引入多余抽象/接口/层级。

## 仓库结构与职责

先阅读 [总体架构](../docs/Architecture-Overview.md)，不要从项目名称推断未定义的层级。
- AI、Logging、Messaging、Persistence 是独立模块目录，各自包含 docs/src/test 和 slnx。
- 小型独立组件暂留根 src/test；根解决方案用于全仓库开发。
- 不要为了统一 src/test 的外观把多项目模块拆散；目录分组不改变实际依赖方向。
- Core / Domain.Primitives 提供通用辅助与小契约，模块专用契约留在模块的 Abstractions 或合适位置。
- samples/Reference 是完整组件接入示例，Domain / Application / Persistence / Web / DatabaseMigrator 各有职责；不是底层框架依赖。
- samples/Lab 是独立教学 Host，具体业务、演示和旧练习数据模型不能回流共享模块。
- 共享组件不得依赖 Reference、Lab 或具体 User/Order 模型。模块解决方案可以显式列出测试所需的依赖项目。
- 公共契约注释说明行为和限制；非直观逻辑旁说明原因；跨文件取舍写入模块 docs。
## 适度原则（防止过度设计）
在引入新抽象前，先判断复杂度：
- ✅ 逻辑简单（1-2 个分支、无持久化策略差异、无跨聚合协作）：允许直接实现为清晰的方法/类，不强行引入 Repository/DomainService/Handler 套娃。
- ✅ 逻辑中等（规则可变、需要测试隔离、跨模块复用）：引入接口/策略/用例类。
- ✅ 逻辑复杂（聚合一致性、跨聚合协作、需要领域事件/规则对象）：按 DDD 完整建模。

默认偏好：**先简单、可测试、可演进**，不要一次性抽象到“未来可能需要”。

## 依赖方向

- 业务 Domain 可引用通用领域契约，不依赖 Host、ORM 或消息框架。
- Application 依赖领域与中立端口；Persistence 实现应用端口并拥有业务模型映射和迁移。
- Web 是组合根，可以选择具体适配器，但不能把业务规则写进端点。
- Core → Domain.Primitives 保持不变；模块按实际需要依赖 Core，不强制所有模块经过 Core。
- Reference 与 Lab Host 互不引用，Lab 可复用 Reference 的示例业务类库。
- 架构检查以项目引用和源码根归属为依据，不要求所有共享代码都在根 src。
## SOLID & 代码风格
- 单一职责：一个类/方法只做一件事；命名体现意图
- 依赖倒置：上层依赖接口/抽象（放 Core 或 AppService/Domain 合适位置）
- 接口隔离：避免“万能接口”
- 保持可测试性：业务规则优先在 Domain/AppService，可用单元测试覆盖

## 错误处理与结果建模
- 业务失败用可表达的领域/应用结果（例如 Result / ErrorCode / DomainException）
- Web 层负责把错误映射为 HTTP 状态码与响应体
- 不要在 Domain 层返回 HTTP / IActionResult / DTO

## 生成代码时的输出要求
当你生成新代码或做较大改动时：
1) 先简述放在哪一层、为什么
2) 说明依赖关系是否符合分层
3) 提供必要的单元测试建议（至少给出测试点/边界条件）

## 项目指南
- For temporary data import endpoints in the Lab only, prefer using the built-in `ExcelHelper` to map uploaded files into classes, and perform direct database updates in the controller via `[FromServices]`-injected DbContext instead of adding repository methods.

## Git 操作指南

### 基本原则
- 不要在用户未明确要求的情况下主动执行 `git commit`、`git push`、`git reset --hard`、`git clean` 等会改变仓库状态或远端状态的操作。
- 执行或建议执行 `git commit`、`git push` 前，必须先检查本次改动中是否包含敏感信息。
- 提交前应尽量保证改动范围清晰、职责单一，避免把无关修改混在同一次提交中。
- 生成提交信息时，应根据当前 Git 用户决定提交语言。

### 敏感信息检查
在执行或建议执行 `git commit`、`git push` 前，必须检查暂存区和本次改动中是否包含敏感信息。

重点检查内容包括但不限于：
- 密码、Token、API Key、Secret Key、Access Key
- 数据库连接字符串、Redis 连接字符串、MQ 连接字符串
- 私钥、证书、`.pfx`、`.pem`、`.key` 等文件或内容
- Cookie、Session、Authorization Header
- 内网地址、生产环境地址、生产账号信息
- 用户隐私数据、身份证号、手机号、邮箱、真实客户数据
- `.env`、`appsettings.Production.json`、`appsettings.*.json` 中的敏感配置
- 临时调试日志中打印的业务数据或凭据

建议检查命令：git diff --cached
git diff
git status --short
必要时可结合关键词搜索：
git diff --cached | grep -Ei "password|passwd|pwd|token|secret|apikey|api_key|accesskey|access_key|connectionstring|authorization|bearer|private key"
规则如下：
- 如果发现明确的敏感信息，必须停止提交或推送，并提醒用户移除、脱敏或改用安全配置方式。
- 如果发现疑似敏感信息，不能自行判断为安全，必须交给用户手动确认是否一定要提交。
- 如果用户明确确认要提交疑似敏感信息，应在回复中说明风险，并优先建议改为配置项、环境变量、密钥管理服务或本地未跟踪文件。
- 不要自动删除、改写或脱敏用户文件，除非用户明确要求。
- 不要在回复中完整复述敏感信息原文；必要时只展示脱敏片段，例如 `sk-****abcd`。
- 敏感信息检查通过后，才可以继续生成 commit message、执行 commit 或建议 push。

### Git 用户与提交语言
提交代码或生成 commit message 时，先判断当前 Git 用户：git config --get user.name
规则如下：
- 如果 Git 用户是 `ms27946`，commit message 使用英文。
- 否则视为公司账户，commit message 使用中文。
- 如果无法确认 Git 用户，不要猜测；应提醒用户确认或手动提供提交语言。

### Commit Message 格式
提交信息统一使用以下格式：<type>: <summary>允许在必要时添加 scope：<type>(<scope>): <summary>
示例：
feat: add temporary data import endpoint
fix: correct order status mapping
opt: improve Excel import performance
refactor: simplify domain validation logic
中文示例：
feat: 新增临时数据导入接口
fix: 修复订单状态映射错误
opt: 优化 Excel 导入性能
refactor: 简化领域校验逻辑
### 常用提交类型
- `feat`: 新功能
- `fix`: 缺陷修复
- `opt`: 优化，包括性能优化、体验优化、代码局部改进
- `refactor`: 重构，不改变外部行为
- `docs`: 文档修改
- `test`: 测试相关修改
- `style`: 格式调整，不影响代码逻辑
- `chore`: 构建、配置、依赖、脚手架等非业务修改

### 提交信息要求
- summary 使用简洁的一句话描述本次提交的核心改动。
- 不要使用含糊描述，例如 `update code`、`fix bug`、`修改问题`。
- 不要在提交信息中加入无关信息，例如工具署名、聊天记录、临时说明。
- 如果一次改动包含多个不相关目的，应建议拆分为多个提交。

### 语言示例
当 Git 用户为 `ms27946`：feat: add customer import validation
fix: resolve duplicate order creation
opt: reduce database queries during import
refactor: extract order status conversion logic
当 Git 用户不是 `ms27946`：feat: 新增客户导入校验
fix: 修复重复创建订单问题
opt: 减少导入过程中的数据库查询
refactor: 抽取订单状态转换逻辑