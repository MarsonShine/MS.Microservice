# EF 软删除：在登记时保留实体的泛型类型

旧扩展接收 `IMutableEntityType`，丢失编译期实体类型后，再用 `GetMethod`、`MakeGenericMethod` 和 `Invoke` 找回泛型过滤表达式。这些操作发生在 EF 模型初始化阶段，本次归为 **AOT compatibility**，不是每次查询都执行反射的性能问题。

新扩展直接接收 `EntityTypeBuilder<TEntity>`，编译器已经知道 `TEntity` 满足 `ISoftDeleted`。可以直接声明过滤表达式并登记索引：

```csharp
modelBuilder.Entity<User>().AddSoftDeletedQueryFilter();
// 内部：HasQueryFilter(row => row.DeletedAt == null)
//       HasIndex(row => row.DeletedAt)
```

Lab 中只有 `User` 实现软删除接口，因此在原有 `EnabledSoftDeleted` 条件下明确登记它，删除“扫描模型后逐个反射闭合泛型”的调用链。以后新增软删除实体，必须在模型配置中显式登记。该扩展仍设置原来的未命名过滤器，没有改变其他过滤器的组合策略。

表达式树本身保留，因为 EF 需要翻译它；生产代码不调用 `Compile()`。`Legacy/Persistence/SoftDeleteRegistration.cs` 与 `Static/Persistence/SoftDeleteRegistration.cs` 独立展示动态与静态的构造方式，完整旧扩展保存在同目录 `.cs.txt` 中。

测试通过真实 EF 查询验证：null 可见，过去或未来的非 null 删除时间都不可见，`IgnoreQueryFilters()` 可以读取全部记录，清空删除时间后重新可见。另检查普通实体不受影响、DeletedAt 索引存在且不重复、Lab 开关启用/关闭行为。仅构造了相同过滤器和索引，不涉及数据库结构迁移；此结论不代表 EF 和整个应用已完成 NativeAOT 发布验证。
