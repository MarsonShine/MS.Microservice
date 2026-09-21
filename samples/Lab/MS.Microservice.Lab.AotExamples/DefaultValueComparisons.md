# 默认值与实体键：在方法入口保留类型

旧实现先把值放进 object，再用 Activator 创建对应的默认值。A04 的第一版把 Activator 换成了 `BoxedDefaults` 类型登记表，消除了动态构造，却仍要求调用方先装箱；实体的 `GetKeys(): object[]` 还会分配数组。泛型实体已经知道 TId，再走这条路径会把有用的类型信息丢掉。

当前实现从签名开始调整：默认比较接收 `T`，实体辅助方法接收 `IEntity<TKey>`，直接读取 `TKey Id`。生产 `IEntity` 只保留标记用途，删除 GetKeys；非泛型 EntityEquals/HasDefaultKeys、IsDefaultBoxedValue、RegisterDefaultValue 和 BoxedDefaults 均已移到学习区，不作为运行时兼容回退。

```csharp
bool isDefault = TypeHelper.IsDefaultValue(entity.Id);
bool same = EntityHelper.EntityEquals(first, second); // first/second: IEntity<TKey>
bool temporary = EntityHelper.HasDefaultId(entity);
```

比较使用 `EqualityComparer<TKey>.Default`，无需类型查表或手动登记。非可空 int/long 的负数临时键仍由 EntityHelper 处理，但使用 `Comparer<TKey>.Default` 比较，不再调用接收 object 的 Convert 重载。typeof(TKey) 的两个判断只选择这条已有领域规则，不构造类型、不扫描成员，也不把键值转换成 object。

## 声明类型决定默认值

`default(int?)` 是 null，`int?` 的 0、负数和其他非 null 值都表示已赋值；`Guid?` 的 Guid.Empty 同理。这里有意退出旧“装箱后按底层类型找默认值”的规则。Lab 的 IsTransient 同样采用声明类型的 default；不再把可空零值当成未赋值。

普通 default 比较不会执行结构体显式无参构造函数。`new MyKey()` 可以赋非零值，而 `default(MyKey)` 仍是零初始化；本次没有用 new T() 替代 Activator。

## 复合键保持一个类型化值

```csharp
public readonly record struct OrderKey(Guid TenantId, long Number);
public sealed class Order : IEntity<OrderKey>
{
    public OrderKey Id { get; set; }
}
```

复合键整体与 `default(OrderKey)` 比较，字段相等由类型化值相等性决定。不再把每个字段拆成 object[]，也不沿用旧数组路径“相同的部分默认字段就拒绝相等”的规则。部分字段的业务有效性应在实体/键的创建或验证逻辑中约束，不从 boxed 数据推断。拥有自定义相等语义的值类型应实现 IEquatable<T> 或使用适当的类型化比较器；泛型签名本身不保证任意用户自定义 Equals 实现都没有分配。

## 旧代码和真实对照

`Legacy/Defaults` 保留最初 Activator 机制。`Legacy/EntityKeys` 保存 A04 完整可编译的 IEntity、EntityHelper 和 TypeHelper，包括 BoxedDefaults；旧数组/装箱规则的回归测试也移动到学习测试项目。`Static/EntityKeys` 独立实现完整类型化判断逻辑，不转调生产 EntityHelper。两套实现使用同一输入比较普通键结果，另明确断言 nullable 零值行为的差异。

当前生产测试覆盖 int/long 临时键、null、空字符串、可空零值、枚举、Guid、带构造函数的自定义键、类型化复合键、同一实例、基类/派生类和不相关实体类型。预热后用 GC.GetAllocatedBytesForCurrentThread 检查标量辅助方法和 Lab 的完整 Equals/IsTransient/GetHashCode 路径；测试模型的键类型为上述受测类型，不把结果推广成所有 T、所有运行时和任意用户相等实现的性能保证。

这是从调用签名到比较路径的局部改动，不需要为每个键创建映射表，也不需要 SG 来生成一个泛型默认比较。SG 可以在模型数量大时生成其他重复代码，但不是消除这条装箱链的前提。没有执行 NativeAOT publish。

本机 .NET 10 实测：同一对 int 键实体，预热 1,000 次后比较 10,000 次，旧实现分配 1,680,000 字节，新实现分配 0 字节。受测泛型辅助路径和 Lab 完整实体路径的 int、long、Guid、nullable、枚举及 record struct 键也通过零分配断言。这是托管分配证据，不是吞吐量或 NativeAOT 发布性能结论。
