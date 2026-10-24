# 事件存储契约

本组件仅定义事件信封、事件存储、快照和投影检查点接口，不依赖具体业务模型、EF Core 或数据库 Provider。

`IEventStore.ReadAllAsync` 保留原有语义：返回指定全局位置之后的全部匹配事件，不隐式截断。已有实现无需新增成员；调用方也不能假定此方法已有内存上限。

需要分页的实现可另外提供 `IEventBatchReader.ReadBatchAsync`。`take` 默认 100，允许 1–1000；按 `GlobalPosition` 升序读取，游标条件是严格大于 `afterGlobalPosition`。`streamType` 非空白时按该类型筛选。下一页使用上一页最后一个信封的 `GlobalPosition`，不能简单给游标加上条数，因为位置可能有间隔。

页大小约束事件数量，不约束单个事件的序列化大小，也不表示读取开始时的固定快照。持续写入时，循环读取可能继续处理新事件；调用方应传递取消令牌并自行决定执行窗口。

Lab 中的 PostgreSQL 适配器同时实现两个接口，并在同一 DI scope 中映射到同一个实例。订单投影的事务、跟踪与使用边界见 [Lab Infrastructure](../../samples/Lab/MS.Microservice.Lab.Infrastructure/README.md)。
