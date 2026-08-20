# 从 Some / None 到 Map / Bind / Apply：本项目函数式核心类型指南

本文以 `src/MS.Microservice.Core/Functional/` 中的实际实现为准，解释以下概念：

- `Some` / `None`（问题中的 `One` 应为 `None`，项目中没有 `One` 类型）
- `Option`
- `Either`
- `Exceptional`
- `Match`
- `Unit`
- `Map`
- `Bind`
- `Apply`

目标不是只记住几个 API，而是建立一个统一的函数式编程心智模型：**用类型明确表达计算可能发生的情况，再用组合操作传递这些计算语义。**

---

## 一、先抓住主线：值之外，还有“计算语义”

普通函数只描述值的变化：

```text
T  --f-->  R
```

例如：

```csharp
static int Length(string text) => text.Length;
```

但真实业务中的计算通常还附带一种语义：

- 可能没有值；
- 可能成功，也可能有一个业务错误；
- 可能正常返回，也可能抛异常；
- 操作已经完成，但没有有意义的返回数据。

本项目用类型把这些情况写进函数签名：

| 类型 | 它表达的计算语义 | 可能状态 |
| --- | --- | --- |
| `Option<T>` | `T` 可能存在，也可能缺失 | `Some(T)` / `None` |
| `Either<L, R>` | 计算可能失败；失败和成功都有明确值 | `Left(L)` / `Right(R)` |
| `Exceptional<T>` | 计算可能抛出异常 | `ExceptionThrown(Exception)` / `Success(T)` |
| `Unit` | 计算已完成，但没有有意义的数据要返回 | 只有 `()` 一个值 |

可以暂时把它们想象成“盒子”，但不要把盒子理解成单纯包装。盒子的关键是它携带了**规则**：

- `Option` 的规则是：没有值时跳过后续值变换；
- `Either` 的规则是：`Left` 时跳过成功路径并保留错误；
- `Exceptional` 的规则是：异常状态沿管道传播；
- `Validation` 的 `Apply` 规则则是：独立验证可以把多个错误累积起来。

所以，更准确的词不是“盒子”，而是**计算上下文**。

---

## 二、构造器与完整类型：不要把 Some 和 Option 混为一谈

### 2.1 `Some<T>` 与 `NoneType` 是两个状态的构造器

在本项目中：

```csharp
Option<int> some = F.Some(42);
Option<int> none = F.None;
```

可以读成：

```text
Option<T> = Some(T) | None
```

竖线 `|` 表示“二选一”，不是按位或。这在函数式编程中叫**和类型**（sum type）或**可辨识联合**（discriminated union）。

- `Some(42)` 证明值存在；
- `None` 明确表示值不存在；
- 两种状态不可能同时出现；
- 使用时必须面对这两种可能。

`Some<T>` 不允许包含 `null`。项目中的构造器会调用 `ArgumentNullException.ThrowIfNull`：

```csharp
F.Some<string>(null!); // 抛 ArgumentNullException
```

缺失值应写成 `None`，而不是 `Some(null)`。这使“存在”与“缺失”不再含糊。

### 2.2 `Some<T>` 不是完整的 `Option<T>`

项目中的 `F.Some(42)` 实际返回 `Some<int>`，然后通过隐式转换成为 `Option<int>`；`F.None` 返回 `NoneType`，也通过隐式转换成为任意 `Option<T>` 的 `None`。

因此，建议在声明时写出目标类型：

```csharp
Option<int> age = F.Some(42);
Option<int> missingAge = F.None;
```

要注意 `var`：

```csharp
var age = F.Some(42); // 推断为 Some<int>，不是 Option<int>
```

如果接下来要调用 `Option` 的 `Map`、`Bind` 等操作，应显式声明为 `Option<int>`，或进行显式转换。

### 2.3 `Option<T>` 也支持值的隐式转换

项目实现了从 `T?` 到 `Option<T>` 的隐式转换：

```csharp
Option<string> a = "hello"; // Some("hello")
Option<string> b = null;    // None
```

这在适配旧接口时方便，但在核心业务代码里，显式使用 `F.Some` / `F.None` 往往更能表达意图。

另外，`Option<T>` 是只读结构体，`default(Option<T>)` 就是 `None`。

---

## 三、Option：把“可能没有”变成类型的一部分

假设查找用户可能找不到。传统签名是：

```csharp
User? FindUser(int id);
```

调用方可能忘记检查 `null`。使用 `Option` 后：

```csharp
Option<User> FindUser(int id);
```

签名直接告诉调用方：“这里有两种合法结果。”缺失不再是藏在引用里的特殊值，而是类型的一种正式状态。

`Option` 适合表达：

- 数据库查询没有匹配项；
- 可选配置不存在；
- 文本无法解析成目标值，但不需要解释原因；
- 某个业务字段本来就是可选的。

如果失败时需要携带“为什么”，`Option` 的信息就不够了，应使用 `Either<Error, T>` 或 `Validation<T>`。

---

## 四、Either：失败原因与成功值二选一

`Either<L, R>` 可以读成：

```text
Either<L, R> = Left(L) | Right(R)
```

本项目遵循常见约定：

- `Left` 表示错误；
- `Right` 表示成功；
- `Map`、`Bind` 默认只作用于 `Right`，所以称为 **right-biased**。

例如：

```csharp
Either<Error, User> ok = F.Right(user);
Either<Error, User> failed = F.Left(
    Error.Validation("用户信息不合法"));
```

为什么不只返回 `bool`？因为 `bool` 只能说“失败了”，不能说明失败原因，也不能在成功时携带业务值。

为什么不总是抛异常？因为“账号已存在”“库存不足”“输入不合法”通常是业务流程中可预期的结果，不是程序机制突然失控。把它们写成 `Left(Error)`，调用方能从签名上看见并组合处理。

项目还提供：

- `Map`：变换 `Right`；
- `MapLeft`：变换 `Left`；
- 双参数 `Map(left, right)`：同时定义两边的变换；
- `Bind` / `BindAsync`：串联可能返回 `Left` 的后续计算；
- `Where`：成功值不满足条件时，用工厂生成 `Left`；
- `Match`：最终同时处理 `Left` 和 `Right`。

虽然 `Either` 暴露了 `.Left` 和 `.Right` 属性，但在错误状态读取 `.Right`，或在成功状态读取 `.Left`，都会抛异常。除非已经由控制流可靠地检查了状态，否则优先使用 `Match`。

还有一个项目实现层面的细节：`Either<L, R>` 是结构体，但它没有像 `Option<T>` 那样把 `default` 明确定义成一个合法的空状态。`default(Either<L, R>)` 会表现得像 `Left`，内部却没有经过 `F.Left` 初始化。不要把它当成正常错误值，应始终通过 `F.Left` / `F.Right` 构造。

---

## 五、Exceptional：Either&lt;Exception, T&gt; 的专用版本

项目对它的定义非常直接：

```text
Exceptional<T> ≈ Either<Exception, T>
Exceptional<T> = ExceptionThrown(Exception) | Success(T)
```

它适合把一个可能抛异常的调用变成可组合的普通返回值：

```csharp
Exceptional<int> result = ExceptionalExtensions.Try(
    () => int.Parse("123"));
```

若调用成功，得到 `Success(123)`；若抛异常，得到 `ExceptionThrown(exception)`。

异步操作可使用 `TryAsync`：

```csharp
Task<Exceptional<string>> result = ExceptionalExtensions.TryAsync(
    () => File.ReadAllTextAsync(path));
```

`Exceptional<T>` 自身也有 `Match`、`Map` 和 `Bind`，行为与 right-biased 的 `Either<Exception, T>` 相同。

同理，不应使用 `default(Exceptional<T>)` 表示异常或成功；应使用 `F.Success`、`F.ExceptionThrown`、`Try` / `TryAsync`，或项目提供的隐式转换创建有效状态。

### Exceptional 与 Either&lt;Error, T&gt; 如何选

| 场景 | 更合适的类型 |
| --- | --- |
| 账号已存在、参数不合法、库存不足 | `Either<Error, T>` |
| 调用第三方库可能抛异常，暂时要把异常纳入管道 | `Exceptional<T>` |
| 只关心有或没有，不需要失败原因 | `Option<T>` |

`Exceptional` 不是让异常变成业务分支的借口。通常应在基础设施边界捕获异常，再把已理解的失败转换成领域可理解的 `Error`。未预期、无法恢复的异常仍应由系统的统一异常处理机制记录和处置。

---

## 六、Match：安全地“离开计算上下文”

`Match` 是理解这些类型的钥匙。

对 `Option<T>`：

```csharp
string text = maybeAge.Match(
    none: () => "年龄未知",
    some: age => $"年龄：{age}");
```

对 `Either<Error, User>`：

```csharp
IResult response = result.Match(
    left: error => Results.BadRequest(error.ToDisplayMessage()),
    right: user => Results.Ok(user));
```

对 `Exceptional<T>`：

```csharp
string message = result.Match(
    exception: ex => $"读取失败：{ex.Message}",
    success: value => $"读取成功：{value}");
```

### Match 究竟做了什么

以 `Option<T>` 为例，它的核心签名是：

```text
Match : (() -> R, T -> R) -> R
```

也就是提供：

- 一个处理 `None` 的函数；
- 一个处理 `Some(T)` 的函数；
- 两个分支最终必须汇合成同一种结果 `R`。

`Match` 的价值是**穷举处理**：调用方不能只写成功路径而假装失败状态不存在。

### Match 与 Map / Bind 的边界

- `Map`、`Bind`、`Apply` 让计算继续留在上下文中；
- `Match` 通常在管道末端，把上下文折叠成 HTTP 响应、UI 文本、异常或其他最终结果。

因此，`Match` 才最接近“安全拆盒”。但它不是偷偷读取内部值，而是要求你明确处理全部状态。

项目中的很多其他操作都可以由 `Match` 定义。例如 `Option.Map` 的本质就是：

```csharp
return option.Match(
    none: () => F.None,
    some: value => (Option<R>)F.Some(f(value)));
```

可以把 `Match` 看成这些和类型的**消解器**（eliminator）或**折叠操作**（fold）。

---

## 七、Map：盒内改值，结构与语义不变

### 7.1 类型签名

给定普通函数：

```text
f : T -> R
```

`Map` 把它提升为：

```text
Map(f) : F<T> -> F<R>
```

这里的 `F` 不是项目里的工厂类 `F`，而是泛指 `Option`、`Either`、`Exceptional` 等上下文。

对 `Option`：

```text
Some(t).Map(f) = Some(f(t))
None.Map(f)    = None
```

示例：

```csharp
Option<string> name = F.Some("Alice");
Option<int> length = name.Map(text => text.Length);
// Some(5)
```

如果 `name` 是 `None`，lambda 不会执行，结果仍是 `None`。

对 `Either<L, R>`：

```text
Right(r).Map(f) = Right(f(r))
Left(l).Map(f)  = Left(l)
```

对 `Exceptional<T>`：

```text
Success(t).Map(f)         = Success(f(t))
ExceptionThrown(e).Map(f) = ExceptionThrown(e)
```

### 7.2 Map 不会“平铺”

假设后续函数自己就返回 `Option<int>`：

```csharp
static Option<int> ParsePositive(string text)
{
    if (int.TryParse(text, out var value) && value > 0)
        return F.Some(value);

    return F.None;
}
```

如果用 `Map`：

```csharp
Option<string> input = F.Some("42");
Option<Option<int>> nested = input.Map(ParsePositive);
```

过程是：

```text
Some("42")
  -> 调用 ParsePositive("42") 得到 Some(42)
  -> Map 再保留外层 Option
  -> Some(Some(42))
```

这是正确的 `Map` 行为。`Map` 的职责就是保留外层结构，所以它不会擅自消除嵌套。

### 7.3 什么时候用 Map

当 lambda 返回的是**普通值**时用 `Map`：

```csharp
option.Map(T => R)
```

典型场景：

- 取属性；
- 格式化；
- 数值运算；
- DTO 映射；
- 把成功结果从一种值转换成另一种值。

`Map` 最好接收纯函数：相同输入产生相同输出，不修改外部状态。日志、写数据库、发消息等副作用应尽量放在边界处，通过 `ForEach`、`Tap` 或明确的基础设施函数处理。

---

## 八、Bind：Map 之后消除一层同类嵌套

### 8.1 类型签名

`Bind` 接受的不是普通函数 `T -> R`，而是“会产生上下文”的函数：

```text
f : T -> F<R>
Bind(f) : F<T> -> F<R>
```

对 `Option`：

```text
Some(t).Bind(f) = f(t)
None.Bind(f)    = None
```

刚才的解析示例改用 `Bind`：

```csharp
Option<string> input = F.Some("42");
Option<int> parsed = input.Bind(ParsePositive);
// Some(42)，不是 Some(Some(42))
```

### 8.2 “平铺 / 展平”到底指什么

在概念上：

```text
Bind(f) = Map(f) + Flatten
```

其中：

```text
Map(f)  : F<T> -> F<F<R>>
Flatten : F<F<R>> -> F<R>
```

所以 `Bind` 常被叫作：

- `FlatMap`；
- `SelectMany`；
- monadic bind。

这里的“展平”非常具体：**只消除一层相同上下文的嵌套**。

```text
Option<Option<R>> -> Option<R>
Either<L, Either<L, R>> -> Either<L, R>
```

它不是：

- 从容器里强行取值；
- 把对象展开成属性列表；
- 把集合递归拍平成任意深度；
- 忽略失败状态。

失败状态仍然按照该上下文的规则传播。`Option` 遇到 `None` 就短路，`Either` 遇到 `Left` 就保留原错误。

### 8.3 Bind 用来表达有依赖的步骤

如果第二步需要第一步的成功值，并且第二步自己也可能失败，就用 `Bind`：

```csharp
Option<User> user = FindUser(userId);

Option<Manager> manager = user.Bind(
    foundUser => FindManager(foundUser.ManagerId));
```

多步流程：

```csharp
Option<int> result = ((Option<string>)F.Some("42"))
    .Bind(ParsePositive)
    .Bind(value => value < 100
        ? F.Some(value)
        : (Option<int>)F.None);
```

只要任一步返回 `None`，后面的 lambda 就不再执行。

`Either` 的业务流程也一样：

```csharp
Either<Error, Receipt> result = FindOrder(orderId)
    .Bind(order => ReserveInventory(order))
    .Bind(reservation => Charge(reservation))
    .Map(payment => CreateReceipt(payment));
```

- `FindOrder`、`ReserveInventory`、`Charge` 都可能返回业务错误，所以用 `Bind`；
- `CreateReceipt` 只是把成功值映射成另一个普通值，所以最后用 `Map`。

### 8.4 LINQ 中的 Bind

项目给 `Option` 和 `Either` 实现了 `Select` / `SelectMany`：

- `select` 对应 `Map`；
- 多个 `from` 对应 `Bind` 加最终的 `Map`。

```csharp
Option<string> description =
    from age in maybeAge
    from name in maybeName
    select $"{name} is {age}";
```

这不是另一套机制，只是 C# 为 `Map` / `Bind` 提供的查询语法糖。

---

## 九、Apply：函数也在上下文中，组合独立输入

### 9.1 类型签名

`Apply` 的输入看起来有点特别：

```text
Apply : F<Func<T, R>> -> F<T> -> F<R>
```

也就是：

- 函数在上下文里：`F<Func<T, R>>`；
- 参数也在同一种上下文里：`F<T>`；
- 把二者组合后，结果仍在该上下文里：`F<R>`。

对 `Option`：

```text
Some(f).Apply(Some(t)) = Some(f(t))
None.Apply(Some(t))    = None
Some(f).Apply(None)    = None
```

项目中的核心实现可概括为：

```csharp
return optionalFunction.Match(
    none: () => F.None,
    some: function => argument.Map(function));
```

### 9.2 多参数示例

假设构造对象需要两个彼此独立的可选输入：

```csharp
public sealed record Person(string Name, int Age);

Func<string, int, Person> create =
    (name, age) => new Person(name, age);

Option<string> maybeName = F.Some("Alice");
Option<int> maybeAge = F.Some(30);

Option<Person> person = maybeName
    .Map(create)
    .Apply(maybeAge);
```

为什么先 `Map`？项目为二元函数提供了重载：

```text
maybeName.Map(create)
    : Option<Func<int, Person>>
```

它把第一个参数应用进去，并通过柯里化留下“还需要一个 `int`”的函数。然后：

```text
Option<Func<int, Person>>.Apply(Option<int>)
    : Option<Person>
```

任意输入为 `None`，结果都是 `None`。

### 9.3 Apply 与 Bind 的关键差异：独立还是依赖

`Bind` 中，后一个计算由前一个成功值决定：

```csharp
FindUser(userId)
    .Bind(user => FindOrders(user.Id));
```

`FindOrders` 的输入依赖 `FindUser` 的结果，所以必须先得到用户。

`Apply` 中，多个输入通常可以独立计算：

```csharp
Func<string, int, Person> createPerson =
    (name, age) => new Person(name, age);

F.Valid(createPerson)
    .Apply(ValidateName(name))
    .Apply(ValidateAge(age));
```

姓名验证和年龄验证互不依赖。它们只是最后共同构造 `Person`。

这一区别在 `Option` 上不总是显眼，因为 `Option.Apply` 和 `Option.Bind` 都会在 `None` 时短路；但在本项目的 `Validation<T>` 上非常重要：

- `Bind` 适合有先后依赖的验证，遇到失败后无法继续下一步；
- `Apply` 适合彼此独立的验证，可以把多个 `Invalid` 的错误合并，一次返回给调用方。

项目中的 `CountryCode.CreatePhoneNumber` 就是实际示例：

```csharp
public static Validation<PhoneNumber> CreatePhoneNumber(
    NumberType type,
    CountryCode countryCode,
    Number number)
    => F.Valid(Create)
        .Apply(ValidNumberType(type))
        .Apply(ValidCountryCode(countryCode))
        .Apply(ValidNumber(number));
```

这里三个验证互不依赖，因此用 `Apply` 聚合结果比用 `Bind` 更能表达意图。

### 9.4 Apply 不是普通的函数调用

普通函数调用：

```text
Func<T, R> + T -> R
```

`Apply`：

```text
F<Func<T, R>> + F<T> -> F<R>
```

它的意义是：**在不逃离上下文的情况下完成函数应用，并保留该上下文的规则。**

### 9.5 项目中同名的 Apply：先看接收者类型

本项目不只在 `Option` / `Validation` 上使用 `Apply`，因此看到方法名时要先看点号左边是什么类型。

普通 `Func` 上的 `Apply` 表示**部分应用**：先固定一个参数，得到等待剩余参数的新函数。

```csharp
Func<int, int, int> add = (left, right) => left + right;
Func<int, int> addTen = add.Apply(10);

int result = addTen(32); // 42
```

这里没有 `Option` 或错误语义，只是在变换函数。`Curry` 也服务于类似目标：把多参数函数改造成可以逐个接收参数的形式。

而 `Option<Func<T, R>>.Apply(Option<T>)` 是本章介绍的应用函子操作：函数和参数都处于 `Option` 上下文。`Validation` 和 `Task` 上的 `Apply` 也具有相同的类型形状，但各自保留自己的上下文语义：

```text
Func.Apply        ：普通函数的部分应用
Option.Apply      ：任意一方为 None，结果为 None
Validation.Apply  ：组合独立验证，并累积错误
Task.Apply        ：等待异步函数和异步参数，再应用函数
```

它们不是毫无关系的重名：核心都与“把函数应用到参数”有关，只是上下文层次不同。

---

## 十、Map、Bind、Apply、Match 一张表分清

### 10.1 先学会读类型表达式

这组表达式非常适合作为速记卡：

```text
Map   : (M<T>, T -> R)    -> M<R>
Bind  : (M<T>, T -> M<R>) -> M<R>
Apply : (M<T -> R>, M<T>) -> M<R>
```

各符号的含义是：

```text
T、R  普通值的类型；T 是变换前的类型，R 是结果类型
M<T>  带有某种计算语义的 T
->    “输入到输出”，即函数映射
(...) 多个输入
```

例如，`M<T>` 可以替换成：

```text
Option<T>          可能有 T，也可能没有
Either<Error, T>   可能有错误，也可能成功得到 T
Exceptional<T>     可能有异常，也可能成功得到 T
Validation<T>      可能有验证错误，也可能得到有效的 T
```

这里用字母 `M` 表示任意计算上下文，不是项目中的静态工厂类 `F`。并不是所有泛型类型都天然支持这些操作：有 `Map` 的上下文具有 Functor 结构，有 `Apply` 的具有 Applicative 结构，有合法 `Bind` 的则具有 Monad 结构。

### 10.2 换成 Option 后是什么样

把上面的 `M` 全部替换为 `Option`：

```text
Map   : (Option<T>, T -> R)
        -> Option<R>

Bind  : (Option<T>, T -> Option<R>)
        -> Option<R>

Apply : (Option<T -> R>, Option<T>)
        -> Option<R>
```

在 C# 中，函数 `T -> R` 由 `Func<T, R>` 表示，所以完全按 C# 类型书写就是：

```text
Map   : (Option<T>, Func<T, R>)
        -> Option<R>

Bind  : (Option<T>, Func<T, Option<R>>)
        -> Option<R>

Apply : (Option<Func<T, R>>, Option<T>)
        -> Option<R>
```

如果按照本项目的扩展方法调用方式来读，则是：

```text
Option<T>.Map  (T -> R)         -> Option<R>
Option<T>.Bind (T -> Option<R>) -> Option<R>

Option<Func<T, R>>.Apply(Option<T>)
    -> Option<R>
```

这三种写法描述的是同一件事，只是视角不同：

- `(Option<T>, T -> R) -> Option<R>` 把接收者和函数都列为输入；
- `Option<T>.Map(T -> R) -> Option<R>` 更接近 C# 扩展方法语法；
- `(T -> R) -> (Option<T> -> Option<R>)` 是柯里化写法，表示先给函数，再得到一个等待 `Option<T>` 的新函数。

例如 `Map` 的以下两个签名在思想上等价：

```text
未柯里化：Map : (M<T>, T -> R) -> M<R>
柯里化  ：Map : (T -> R) -> (M<T> -> M<R>)
```

阅读函数式资料时看到的写法可能不同，不必把它们误认为不同操作。

### 10.3 为什么 Bind 的公式里会多一个 M

`Map` 接收的函数返回普通值：

```text
T -> R
```

`Bind` 接收的函数自己就会产生带上下文的结果：

```text
T -> M<R>
```

如果对后一种函数直接使用 `Map`，外层 `M` 会被保留，函数又产生一个内层 `M`：

```text
Map : M<T> + (T -> M<R>)
      -> M<M<R>>
```

`Bind` 再把这层同类嵌套消掉：

```text
Flatten : M<M<R>> -> M<R>

Bind = Map + Flatten
```

因此可以从类型推导出：

```text
Bind : M<T> + (T -> M<R>) -> M<R>
```

对应 `Option` 就是：

```text
Option<T> + (T -> Option<R>)
    -> Option<Option<R>>   // 如果只 Map
    -> Option<R>           // Bind 帮你展平一层
```

注意：`Flatten` 只消除一层**相同上下文**的嵌套，并没有把值从上下文中强行取出来。

### 10.4 Match 的签名为什么不完全一样

`Map`、`Bind`、`Apply` 都让结果继续留在 `M` 中；`Match` 则需要知道这个具体类型有哪些状态，然后逐一处理。

`Option` 有 `None` 和 `Some` 两个状态：

```text
Option.Match :
    (Option<T>, () -> R, T -> R)
    -> R
```

`Either` 有 `Left` 和 `Right` 两个状态：

```text
Either.Match :
    (Either<L, T>, L -> R, T -> R)
    -> R
```

可以看到，`Match` 的最终结果是普通 `R`，不再是 `M<R>`。所以它通常位于业务管道末端，负责处理所有状态并离开上下文。

### 10.5 总表

| 操作 | 你手里的函数 | 输入 | 输出 | 核心动作 |
| --- | --- | --- | --- | --- |
| `Map` | `T -> R` | `M<T>` | `M<R>` | 改变内部值，保持上下文 |
| `Bind` | `T -> M<R>` | `M<T>` | `M<R>` | 串联有依赖的上下文计算，消除一层嵌套 |
| `Apply` | 已在上下文中的 `M<Func<T,R>>` | `M<T>` | `M<R>` | 在上下文内应用函数，组合独立输入 |
| `Match` | 每个状态各一个处理函数 | `M<T>` | `R` | 穷举所有状态并折叠成最终结果 |

两个辅助签名也值得一起记住：

```text
Pure / Some : T -> M<T>
Flatten     : M<M<T>> -> M<T>
```

`Pure` 是把普通值放入上下文。在 `Option` 中，对应的就是把 `T` 构造成 `Some(T)`；但不同上下文会有不同名称，例如 `Right`、`Success`、`Valid`。

最实用的判断方法是先看 lambda 的返回类型：

```text
返回普通值 R？
    -> Map

返回同类上下文 F<R>？
    -> Bind

已经有多个独立的 F<T>，想交给一个多参数函数？
    -> Map + Apply

准备生成 HTTP 响应、显示文本或其他最终结果？
    -> Match
```

再看几个紧挨着的例子：

```csharp
// 普通值变换：T -> R
Option<int> length = maybeText.Map(text => text.Length);

// 后续也可能缺失：T -> Option<R>
Option<int> number = maybeText.Bind(ParsePositive);

// 两个独立的可选参数交给二元函数
Option<Person> person = maybeName.Map(create).Apply(maybeAge);

// 管道结束，处理所有状态
string output = number.Match(
    none: () => "不是正整数",
    some: value => $"解析结果：{value}");
```

---

## 十一、三种容易混淆的“展开”

### 11.1 取出 / 拆盒：Match

```text
Option<T> --Match--> R
```

处理所有状态后，离开 `Option` 上下文。这是安全的“取出”。

### 11.2 展平一层：Bind

```text
Option<Option<T>> --Flatten--> Option<T>
```

`Bind` 把 `Map` 与这一步合在一起。它仍然留在 `Option` 上下文中。

### 11.3 在盒内调用：Apply

```text
Option<Func<T,R>> + Option<T> --Apply--> Option<R>
```

它没有离开上下文，也不等于简单拆盒；它让函数和参数遵守共同的上下文规则。

记忆短句：

> Map 改值，Bind 接链并减一层，Apply 合并独立输入，Match 收口。

---

## 十二、Unit：把“没有返回值”也变成一个值

`void` 表示“没有值”，而 `Unit` 是一个真正的类型，并且只有一个可能值：

```csharp
Unit.Default // 输出为 ()
```

可以类比：

- `bool` 有两个值：`true`、`false`；
- `Unit` 只有一个值：`()`。

它不携带业务信息，只表示“函数正常完成了”。

传统的副作用操作：

```csharp
Action<string> log = message => Console.WriteLine(message);
```

通过项目的 `ToFunc` 可以转换为：

```csharp
Func<string, Unit> logFunction = log.ToFunc();
```

于是它能放进要求 `Func<T, R>` 的统一组合模型里。本项目也用 `Unit` 表示无返回值操作的成功结果：

```csharp
Either<Error, Unit>
Exceptional<Unit>
Validation<Unit>
```

这比 `bool` 更准确：`Unit` 只表达“正常完成且没有结果数据”，而 `bool` 往往会让人继续猜 `false` 的原因。

但要注意：返回 `Unit` **不会让副作用自动变纯**。写文件、改数据库、打印日志仍然是副作用；`Unit` 只是让这些函数在类型层面拥有统一返回值。函数式设计通常把副作用推到系统边界，让中间的业务变换尽量保持纯粹。

---

## 十三、结合本项目看一条真实管道

`UserDomainService.CreateUserEitherAsync` 同时使用了 `Option`、`Either`、`Match`、`Bind` 和 `Map`。可以按语义逐段读：

### 13.1 先查找：Option

```csharp
var existUser = await _userRepository.FindOptionAsync(...);
```

查询结果可能存在，也可能不存在，所以返回 `Option<User>`，而不是裸 `null`。

### 13.2 过滤后分流：Where + MatchAsync

```csharp
return await existUser
    .Where(existingUser => !existingUser.IsTransient())
    .MatchAsync(
        none: async () => { /* 创建用户 */ },
        some: _ => Task.FromResult(/* 账号冲突 */));
```

- `None`：没有现存用户，可以继续创建；
- `Some`：用户已存在，返回 `Left(Error.Conflict(...))`。

这里 `MatchAsync` 是 `Option` 流程的收口点，两种状态被转换为统一的 `Task<Either<Error, bool>>`。

### 13.3 串联依赖操作：Either.BindAsync

```csharp
return await insertResult.BindAsync(async _ =>
{
    var saveResult = await unitOfWork.SaveChangesEitherAsync(...);
    return saveResult
        .Where(...)
        .Map(_ => true);
});
```

保存动作依赖插入成功，所以用 `BindAsync`：

- 插入是 `Left`，lambda 不执行，错误直接传播；
- 插入是 `Right`，才继续保存；
- 保存也返回 `Either`，`BindAsync` 保持结果只有一层。

### 13.4 最后只改成功值：Map

```csharp
.Map(_ => true)
```

这里不再进行新的可能失败计算，只把成功的 `changed` 映射为 `true`，所以用 `Map`。

整条流程可以概括为：

```text
可能找不到用户（Option）
    -> MatchAsync 处理存在 / 不存在
    -> 可能插入失败（Either）
    -> BindAsync：成功后才能保存
    -> Map：把最终成功值转换为 bool
    -> Match：在更外层转换为 API / Result 表达
```

函数式编程的价值不在于少写几个 `if`，而在于每一步的**失败模型、依赖关系和组合规则都体现在类型与操作里**。

---

## 十四、函数式思想：为什么值得这样写

### 14.1 让非法或遗漏状态更难表达

`Option<T>` 迫使调用者承认值可能缺失；`Either<Error, T>` 迫使调用者承认业务可能失败；`Match` 迫使调用者处理全部状态。

问题没有消失，而是从运行时的意外转变为编译期可见的设计。

### 14.2 组合小函数，而不是反复拆装状态

命令式代码常在每一步写：

```csharp
if (x is null) return ...;
if (!result.Success) return ...;
try { ... } catch { ... }
```

函数式管道把这些重复的控制流交给 `Option`、`Either` 等上下文的 `Map` / `Bind` 实现。业务代码更专注于“成功值下一步做什么”。

这并不是消灭分支，而是把分支集中到可复用的类型规则里。

### 14.3 纯函数让重构和推理更可靠

理想的 `Map` 函数：

```csharp
user => new UserDto(user.Id, user.Name)
```

只由输入决定输出，不修改外部状态。这样的函数容易测试、组合和替换。

有副作用的操作不可避免，但应在边界处显式出现。项目中的 `ForEach` 返回 `Unit`，`Either.Tap` 返回原 `Either`，都在提醒读者：“这里是在观察或执行效果，而不是改变管道中的业务值。”

### 14.4 这些操作不是任意约定，而应满足组合规律

不需要背诵范畴论，但要知道这些规律保护了重构直觉。

`Map` 的两个核心规律：

```text
x.Map(identity) == x
x.Map(f).Map(g) == x.Map(value => g(f(value)))
```

`Bind` 的直觉规律包括：

- 把一个普通值放入上下文再 `Bind(f)`，应与直接执行 `f` 等价；
- `Bind` 一个只负责重新包装的函数，不应改变原计算；
- 改变 `Bind` 链的括号分组，不应改变结果。

如果 `Map` / `Bind` 中的函数偷偷依赖时间、随机数或可变全局状态，这些规律就可能失去实际意义。这也是为什么函数式编程强调纯函数。

---

## 十五、常见误区

### 误区 1：Some 就是 Option

在概念上，`Some` 是 `Option` 的一个状态构造器；在本项目实现上，`F.Some` 返回 `Some<T>`，需要隐式转换为完整的 `Option<T>`。

### 误区 2：None 就等于 null

它们都能表达缺失，但 `None` 是 `Option` 的合法、显式状态，能参与 `Match`、`Map`、`Bind` 等组合；裸 `null` 容易绕过处理。项目允许 `null` 隐式转为 `None`，主要是为了边界适配方便。

### 误区 3：Map 会自动处理任何返回类型

`Map` 当然可以接收返回 `Option<R>` 的函数，但结果会是 `Option<Option<R>>`。需要单层结果时应使用 `Bind`。

### 误区 4：Bind 就是把值取出来

`Bind` 没有离开上下文。它只是让内部值进入一个返回同类上下文的函数，并消除一层嵌套。真正收口通常用 `Match`。

### 误区 5：Apply 比 Bind 更高级，所以应该优先用

两者表达不同结构：有依赖用 `Bind`，独立输入组合用 `Apply`。选择依据是数据依赖，不是“高级程度”。

### 误区 6：Either 和 Exceptional 可以随意互换

`Either<Error, T>` 适合可预期、可解释的失败；`Exceptional<T>` 保留原始异常。业务层长期传播裸异常会把基础设施细节带入领域语义。

### 误区 7：用了这些类型，代码就自动函数式

如果在 `Map` 中修改全局状态、在属性上强取错误分支、到处来回拆盒，仍然很难推理。函数式思想还包括不可变数据、纯函数、显式副作用与基于组合的设计。

---

## 十六、最后的记忆框架

先记类型：

```text
Option<T>       = 有 T | 没有
Either<L, R>    = 错误 L | 成功 R
Exceptional<T>  = 异常 | 成功 T
Unit            = 正常完成，但没有信息要返回
```

再记操作：

```text
Map    ：T -> R       ，盒内改值
Bind   ：T -> F<R>    ，串联有依赖的计算，并消除一层嵌套
Apply  ：F<Func> + F<T>，组合独立的盒装输入
Match  ：处理全部状态  ，在边界收口
```

最后记住函数式编程真正关心的不是“盒子技巧”，而是：

> 让函数签名诚实描述所有可能结果，让小函数保持可预测，再通过稳定的组合规则搭建完整业务流程。

当你再次犹豫用哪个操作时，不要先想术语，只问两个问题：

1. 下一步返回普通值，还是返回同类上下文？
2. 多个输入彼此依赖，还是可以独立得到？

回答完这两个问题，`Map`、`Bind` 和 `Apply` 通常就不会再混淆。

---

## 十七、对应源码索引

- [`Option.cs`](../src/MS.Microservice.Core/Functional/Option.cs)：`NoneType`、`Some<T>`、`Option<T>` 与核心 `Match`。
- [`OptionExtensions.cs`](../src/MS.Microservice.Core/Functional/OptionExtensions.cs)：`Map`、`Bind`、`Apply`、LINQ 支持与边界操作。
- [`Either.cs`](../src/MS.Microservice.Core/Functional/Either.cs)：`Left<L>`、`Right<R>`、`Either<L, R>`。
- [`EitherExtensions.cs`](../src/MS.Microservice.Core/Functional/EitherExtensions.cs)：right-biased 的 `Map` / `Bind` 与异步组合。
- [`Exceptional.cs`](../src/MS.Microservice.Core/Functional/Exceptional.cs) 与 [`ExceptionalExtensions.cs`](../src/MS.Microservice.Core/Functional/ExceptionalExtensions.cs)：异常上下文与 `Try` / `TryAsync`。
- [`Unit.cs`](../src/MS.Microservice.Core/Functional/Unit.cs) 与 [`ActionExtensions.cs`](../src/MS.Microservice.Core/Functional/ActionExtensions.cs)：`Unit` 及 `Action` 到 `Func<..., Unit>` 的转换。
- [`FuncExtensions.cs`](../src/MS.Microservice.Core/Functional/FuncExtensions.cs)：普通函数的 `Apply`、`Curry` 等函数变换。
- [`FAsync.cs`](../src/MS.Microservice.Core/Functional/FAsync.cs)：`Task` 上的 `Map`、`Bind` 和 `Apply`。
- [`Validation.cs`](../src/MS.Microservice.Core/Functional/Validation.cs) 与 [`CountryCode.cs`](../src/MS.Microservice.Core/Functional/CountryCode.cs)：`Apply` 累积独立验证错误的实现与示例。
- [`UserDomainService.cs`](../src/MS.Microservice.Domain/Services/UserDomainService.cs)：`Option` 与 `Either` 在真实业务管道中的组合。
