# 代码优化交付与最终验收

Status: complete

分支：`codex/code-optimization-20261024`。基线：`883b907c733d75093f79a0fa20639023ac3167fd`；实现终点：`cd32a1b`。00审计与01–13实现分别提交，按[分片清单](ledger.md)顺序审核；每片链接包含范围、根因、命令和结果。没有推送或改写已有提交，最后的文档提交仅归档验收与状态。

## 验收结果

环境：Windows，SDK 10.0.402，运行时10.0.13。全解决方案构建产物来自实现终点，之后只有交付文档变化。[机器可读结果](final-results.json)保留48个测试程序集计数、TRX哈希、跳过原因与API包哈希。

| 检查 | 实际结果 |
| --- | --- |
| `dotnet restore --configfile nuget.config` | 退出码0 |
| `dotnet build --no-restore -c Release` | 退出码0；0警告、0错误 |
| `dotnet test --no-build -c Release` | 最终完整运行退出码0；48个程序集，3553通过、0失败、158跳过 |
| 全量API兼容门禁 | 固定883b907基线，16包通过；32个基线/当前包哈希复核一致 |
| API门禁自检 | 复用Slice06：不变/新增通过，删除公开方法触发CP0002且退出码1 |
| 实际NuGet消费者 | 复用Slice06：5包、8项行为通过，无ProjectReference；48个导出源码输入与当前一致，包/脚本/配置哈希复核一致 |
| 架构边界 | 最终全仓与Slice13均42/42通过 |
| harness与交付结构 | check、closeout --complete通过；151个本地链接有效 |

完整测试命令追加`--verbosity quiet --logger trx --results-directory artifacts/validation/code-optimization-final-tests-elevated`以保存结果；源码、断言和测试配置没有调整。还原为quiet模式、退出码0且无输出，因此未生成日志文件。原始构建/测试输出保留在`artifacts/validation/code-optimization-final-*.log`，TRX位于结果目录。

首轮受限环境运行退出码1：3539通过、14失败、158跳过。3项SMTP回环TLS失败发生在Windows临时证书私钥容器导入；10项真实浏览器用例超时，1项子进程测试被脚本执行策略拒绝。相关Notifications、Rendering及其依赖源码相对基线未变。TLS三项和浏览器一项针对探针先通过，随后在允许所需本机资源访问的执行环境中完整重跑，14项均通过。保留首轮结果，不将失败结果改写为通过；没有改机器策略、关闭Chromium沙箱、放宽TLS或修改断言。

API全量报告`artifacts/validation/api-compatibility-final.json`记录的sourceHead为80c0335、source为working-tree：当时新增IEventBatchReader已经纳入包，随后cd32a1b提交了相同受保护源码和Lab投影。此后受保护包输入没有变化。消费者输入则自8489d11起未变，复用而未重复运行；原执行时间保留在结果JSON中。

## 交付范围与边界

已完成AI共享并发/响应预算、Outbox即时认领、音频释放、最终HTTP日志、API/包消费门禁、Core HTTP契约收敛、通用JWT与宿主策略分离、Tasks终态收敛/保留清理、架构检查、单调计时和Lab分页投影。新增能力落在现有组件，没有新增业务模块或第二套调度器。

Slice11的完整验收发现基线已有三处模块解决方案缺项：Media遗漏Processes，Messaging/Persistence遗漏Threading。编辑前明确扩充该片范围，补齐三个.slnx；没有修改生产ProjectReference或降低架构断言。

158项跳过保留原条件，包含真实PostgreSQL/RabbitMQ、Redis、FFmpeg/本地模型、LibreOffice和性能基线等；逐项原因在结果JSON中。未执行部署、真实后端故障矩阵或漏洞专门验收，原远端CI失败未在本次调查，不声明生产就绪或性能提升比例。

Tasks仍要求单执行宿主；终态持久化持续失败时保留执行容量直至恢复或停机。物理保留清理结束该JobId幂等保留期，take不保证数据库扫描工作量上限。Lab分页限制事件条数，不保证单事件字节预算，也不解决并发投影或PostgreSQL序列分配与提交顺序问题。多实例租约、缓存失效广播、密钥轮换仍按原规划的实际需求触发，不在本次实施集合中。

## 收尾记录

harness check退出码0；closeout --complete以883b907为基线检查所有已声明改动，结果passed，0错误、0警告，报告artifacts/validation/code-optimization-closeout.json。151个修改文档中的本地链接检查均存在，git diff --check通过。状态入口已归档为idle/none，本次生产变更与各片声明逐一核对；最后只提交状态入口、清单和本验收记录。必要的原始验证输出保留为忽略工件，未清理未知目录。

## 额度重置后的接续

用户要求当前收尾后停止，待额度重置再继续。剩余实现slice（undo/未完成）：无；00–13均已提交，最后只归档验收记录。恢复后从[分片清单](ledger.md)逐个审核已有提交，不重复实现或重跑已有效的验收。条件性能力仍是未来候选，未被转为本轮未完成slice。
