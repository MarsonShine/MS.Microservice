# 表达式组合与内存委托分开使用

旧 `ExpressionStarter<T>` 隐式转换为 `Func<T,bool>` 时调用 `Compile()`，显式 Compile 重载也是如此。一次看似普通的赋值或 Where 调用可能生成代码；重复转换会重复编译。旧类完整保留在 `Legacy/Linq`。

生产类仍支持 And、Or、Not 和转成 `Expression<Func<T,bool>>`，供查询提供方翻译；移除 Func 隐式转换和两个 Compile 入口。表达式树不是需要删除的对象，隐式运行时编译才是本次退出的行为。

```csharp
// 查询：显式交给 IQueryable 的提供方翻译。
Expression<Func<Order, bool>> query = PredicateBuilder.New<Order>(o => o.Id > 0);
var result = db.Orders.Where(query);
// 内存：直接声明编译期委托，不先造表达式再编译。
Func<Order, bool> predicate = static order => order.Id > 0;
var selected = orders.Where(predicate);
```

`Static/Linq/MemoryFilter` 独立展示内存入口接收普通委托。测试对同一组负数、零、边界和正常值比较旧隐式编译、新委托与保留的组合表达式结果；测试为检查表达式语义而显式使用解释器，生产代码不替调用方编译。不能把解释执行伪装成消除全部 AOT 问题的动态回退。需要运行时语言求值的使用者必须自己明确选择其执行方式。
