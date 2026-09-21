# 为什么删除 ExpressionStarter 的隐式编译入口

旧代码允许这样写：

```csharp
var starter = PredicateBuilder.New<int>(x => x > 0);
Func<int, bool> predicate = starter;
```

第二行看起来只是把一个对象赋给变量，但实际上会执行 `starter` 中表达式的 `Compile()`。这个提交首先要解决的是：调用方没有写编译操作，却在类型转换时承担了把表达式变成可执行委托的开销。

同时删除两个显式 `Compile` 方法，是进一步缩小这个类职责的选择。它并不是修复隐式转换问题所必需的步骤，下面分别解释。

## 相同的 Lambda 写法，可以产生两种不同的东西

```csharp
Func<int, bool> predicate = x => x > 0;
Expression<Func<int, bool>> expression = x => x > 0;
```

第一行得到普通委托，可以直接调用 `predicate(3)`。Lambda 的方法体随项目编译，不需要先创建一棵表达式树，再调用 `Expression.Compile()`。这不表示普通委托完全没有分配，也不表示 JIT 环境不再进行机器码编译。

第二行得到表达式树。它用对象描述“接收参数 x，比较 x 是否大于 0”这段规则，可以被检查、组合或翻译，但不能像委托一样直接调用 `expression(3)`。

`ExpressionStarter<T>` 用来构造第二种东西。它的 And、Or 等操作会拼接表达式，方便逐步组合查询条件。

## 旧转换隐藏了什么工作

[旧实现](Legacy/Linq/ExpressionStarter.cs)中的转换方法是：

```csharp
public static implicit operator Func<T, bool>(ExpressionStarter<T> right)
{
    return right == null ? null! :
        (right.IsStarted || right.UseDefaultExpression)
            ? right.Predicate.Compile()
            : null!;
}
```

因此，对于已经有谓词的 starter，下面两个赋值都会进入 `Compile()`：

```csharp
Func<int, bool> first = starter;   // 转换一次，调用一次 Compile。
Func<int, bool> second = starter;  // 再次转换，再调用一次 Compile。
```

旧类没有保存第一次得到的委托。即使条件没有变化，后一次转换也不会复用前一次结果。

普通 .NET 运行环境支持动态代码时，表达式编译会为这棵树生成可执行实现，涉及表达式处理和代码生成。它与复制一个已有委托引用相比，做的工作多得多。在不支持动态代码的环境中，执行方式还可能不同，后面单独说明。

这个成本按转换次数发生。例如，把 starter 传给内存集合的 `Where` 时，会先转换成委托；随后遍历元素是反复调用这个委托。**不能把它描述成每判断一个元素就编译一次。** 只有反复转换、反复创建这样的筛选调用，才会重复触发这里的工作。

从旧实现可以确认重复转换没有复用结果，但该提交没有给出生产高频调用或耗时测量。删除入口消除了这条隐藏的执行路径，不能据此声称某个业务接口已经获得明显提速。

## 为什么保留表达式，却去掉转成 Func 的便利写法

数据库查询和内存筛选需要的输入不同：

```csharp
// 查询提供方接收表达式树，读取其中的条件并尝试翻译。
Expression<Func<Order, bool>> query =
    PredicateBuilder.New<Order>(order => order.Id > 0);
var result = db.Orders.Where(query);

// 内存集合接收委托，遍历时直接调用。
Func<Order, bool> predicate = static order => order.Id > 0;
var selected = orders.Where(predicate);
```

上例假设 `db.Orders` 是数据库提供方的 IQueryable，`orders` 是内存集合。表达式能否被翻译，还取决于查询提供方及表达式内容；保留表达式不等于保证所有查询都能翻译。

查询提供方需要看得懂规则的结构，表达式树有用。对于源码里已经写好的简单内存判断，直接声明委托就能完成工作。先把同一段规则变成表达式树，再转回委托，多出了一次执行准备。

所以生产 [ExpressionStarter.cs](../../../src/MS.Microservice.Core/Linq/ExpressionStarter.cs)保留了表达式组合，以及到 `Expression<Func<T, bool>>` 的转换，删除了到 `Func<T, bool>` 的隐式转换。原来依赖这个转换的内存调用会编译失败，调用方需要明确选择下一步怎么执行。这是一次有意的兼容性破坏。

## 为什么连显式 Compile 方法也删了

显式写 `starter.Compile()` 时，调用方已经表明要执行编译，它并没有隐藏操作。**如果目标仅是修复隐式转换，保留显式 Compile 完全是可行方案。**

此次整改还采用了“查询表达式组合保留，内存判断显式接收普通委托”的范围约定，因此把两个 Compile 转发方法也移出了生产类，让这个类只负责组织表达式。删除它们并不是证明 `Compile()` 本身没有合理用途。

对于真正运行时构造的规则，仍然可能需要先生成表达式树，再编译或解释。例如规则来自用户选择的字段和运算符，就未必能用一段固定 Lambda 完整替代。此时可以先取出表达式，由使用者选择执行方式和复用范围：

```csharp
Expression<Func<int, bool>> expression = starter;

// 当应用明确选择运行时表达式执行时，在自己的边界上进行。
var predicate = expression.Compile();
var selected = values.Where(predicate);
```

这段写法仍有编译开销；它只是把执行决定交还给调用方，不是本次静态内存筛选的替代方案。若应用需要 NativeAOT，还应单独验证其表达式、执行方式和发布结果。

## 缓存或者解释执行能不能解决

缓存可以减少相同表达式的重复编译，是合理的另一种方案。但 starter 可以继续通过 Start、And、Or 修改谓词，也有可设置的 DefaultExpression。类内若缓存委托，就必须在相关变化后失效，否则可能继续执行旧条件。这次没有引入这套缓存管理，而是移除了自动编译的入口。

AOT 方面也要说准确：.NET 10 的 `Expression<TDelegate>.Compile()` 会检查能否生成动态 IL；不能时可走解释器。`Compile(preferInterpretation: true)` 则在解释器可用时优先选择解释执行。**不能笼统地说所有 Compile 调用在 NativeAOT 下一定失败。** 具体分支可见 [.NET 10 的 LambdaExpression 源码](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/LambdaExpression.cs)。

解释执行仍需要把表达式准备成解释器使用的形式，运行时也仍要执行这些指令。把旧转换改成优先解释，不能消除每次转换都重新准备的工作，也不能自动保证任意表达式涉及的成员都满足裁剪要求。因此，这次没有在隐式转换里默默换一种执行方式。

## 对照测试验证了什么

[学习区对照测试](../../../test/MS.Microservice.Lab.AotExamples.Tests/ExpressionCompilationTests.cs)用 `x > 0 && x < 10`，对比旧隐式转换得到的委托、保留的组合表达式和新的普通委托。输入包含 `-1`、`0`、`1`、`9`、`10`，检查正常值与边界的判断结果一致。

测试中显式使用 `Compile(preferInterpretation: true)`，是为了执行表达式并检查组合语义；它不代表生产类仍提供隐式解释回退。这个测试也没有测量编译耗时，或执行 NativeAOT 发布。

完整旧类留在 [Legacy/Linq](Legacy/Linq/ExpressionStarter.cs)，新的 [MemoryFilter](Static/Linq/MemoryFilter.cs)只是一个接收普通委托的独立教学例子，没有增加新的生产筛选框架。
