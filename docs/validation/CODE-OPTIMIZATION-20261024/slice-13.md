# Slice 13：投影有界批次与批内身份复用

Status: complete

范围：共享EventSourcing分页读取接口；Lab PostgresEventStore、OrderReadModelProjector、相关DI；Lab.Tests真实SQLite持久化投影回归；共享/Lab相关说明；状态/证据。范围外：schema/migrations、Reference业务、分布式投影协调与提交水位协议、旧ReadAll调用方改写。

根因：ReadAll无批上限，projector每event查库，不能看见本批尚未保存的Added新订单，发生同key重复跟踪；跨页若一直跟踪也会随积压增长。不变量：每批事件数有界、同一订单一个模型对象、数据与checkpoint原子提交；失败后同context重试不能重复累加，调用者原有tracked对象不能被全局Clear。

方案：独立IEventBatchReader，不给旧IEventStore新增成员；数据库OrderBy+Take，DI同scope同store。projector要求支持分页接口，每批100，订单Id预取成字典。入口拒绝待保存更改/已有事务；批前保留原tracked投影对象快照，事务失败后恢复，finally只detach本批新增跟踪的模型/checkpoint。保留显式SaveChanges与Checkpoint.Store在同一事务内，逐批持续追赶至短页/空页。

验收：`dotnet test test/MS.Microservice.Lab.Tests/MS.Microservice.Lab.Tests.csproj --no-restore -c Release --verbosity quiet`；架构完整测试；`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.EventSourcing') -ReportPath artifacts/validation/api-compatibility-slice13.json`。

边界：批次限制事件数量而非单事件payload字节；持续写入可能延长单次追赶，可取消。SQLite验证持久化/事务/身份与tracker，不代表PostgreSQL锁、jsonb约束、并发sequence分配和提交顺序已验证或已解决。

结果：完整Lab.Tests 418通过、0失败、15项既有可选环境跳过（总433，约2分54秒），退出码0；新增8项真实SQLite持久化投影测试全部通过。Lab.Infrastructure构建0警告0错误。架构测试42/42通过，0失败0跳过、退出码0。EventSourcing单包API通过，报告artifacts/validation/api-compatibility-slice13.json；最终16包API亦通过，报告artifacts/validation/api-compatibility-final.json，其中新增未跟踪IEventBatchReader已纳入构建。

真实红测：旧projector处理同一新订单OrderCreated+OrderItemAdded时，第二个Add触发相同OrderId重复跟踪InvalidOperationException，1失败。新测试验证真实数据库LIMIT/严格游标/过滤；212事件分100/100/12三批，跨批订单累计正确，逐批跟踪器仅保留caller原对象；checkpoint保存后抛异常时模型/水位一起rollback（新模型及原tracked两种），同context重试无重复；commit完成后回包异常，保留原对象并追加下一事件，最终累计仍正确；脏context和外部事务明确拒绝。

复核：本批快照仅包含本页模型与同名checkpoint；每批AsNoTracking读取数据库值，合并原tracked Current/Original，快照只恢复caller内存，不替代数据库事实。await using事务在finally清理前结束；只detach本批新跟踪投影对象，原事件/snapshot不清空。独立只读复核未发现具体缺陷，git diff --check通过。旧IEventStore不增加abstract成员，ReadAll仍独立全量读取。

环境边界：15项跳过涉及现有FFmpeg/浏览器/LibreOffice等可选环境，未作为通过；真实PostgreSQL并发/隔离及部署未执行。本片未改变Slice06四入口消费者的48个导出源码输入，其5包与脚本/配置hash再次核对一致，复用8项行为证据。
