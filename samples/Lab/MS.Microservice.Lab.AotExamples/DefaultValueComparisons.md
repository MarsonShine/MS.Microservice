# 默认值比较：保留类型信息，而不是构造一个运行时对象

旧 `TypeHelper` 把值装箱为 object，再按运行时 Type 调用 Activator。新接口直接使用 `GetDefaultValue<T>()` 和 `IsDefaultValue<T>(value)`，编译器看到具体类型，无需发现构造器。显式无参结构体构造器可能给字段赋非零值，因此 `new T()` 的结果并不总等于 C# 的 `default(T)`；测试专门区分这两个概念。

实体复合键仍是 object[]，不能直接用 `IsDefaultValue<object>`，否则装箱的整数零会被当成非 null 引用。该路径改用 `IsDefaultBoxedValue`：常见基础值类型已经静态登记，自定义结构体或枚举需在启动时显式调用 `RegisterDefaultValue<MyKey>()`，缺失注册就报错，不反射构造。

```csharp
TypeHelper.RegisterDefaultValue<OrderId>();
bool transient = EntityHelper.HasDefaultKeys(order);
```

泛型 nullable 的默认值是 null，装箱的 nullable 零值则变成整数零。两种入口刻意保留各自语义；实体键和 Lab 的 IsTransient 使用装箱入口，已有 int/long 非正临时主键规则保持在 EntityHelper。

方法名称辅助改为 `GetFullMethodName<Owner>(nameof(Owner.Method))`，由调用方明确指定成员名，不再用反射检查方法存在。类型显示名称等不涉及成员发现或动态构造的 Type API 保留。

完整旧实现与最小可运行对照位于 Legacy/Defaults，普通泛型示例位于 Static/Defaults。测试覆盖结构体构造副作用、缺失/重复注册、Guid、nullable 装箱、临时主键和复合键，验证的是契约，不是“Activator 出现就一定很慢”。
