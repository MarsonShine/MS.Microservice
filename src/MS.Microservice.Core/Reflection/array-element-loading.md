# 查询参数数组的 IL 元素读取

`QueryStringParameters.Dispatch` 默认使用 `IlPropertyAccessorStrategy` 为查询对象生成属性读取委托。数组属性走 `ldlen` 和按下标读取的路径，随后把每个元素作为 `object` 交给 `AppendValue`。原代码对所有一维数组都发射 `ldelem.ref`；它适合 `string[]` 等引用元素，却不能把 `int[]` 中直接存放的值当作对象引用读取。

独立 .NET 10 Release 进程以非空 `int[]` 调用 `Dispatch` 时，原实现触发不可捕获的 `AccessViolationException`；空 `int[]`、`string[]` 和 `List<int>` 不走到同一错误。受影响的不只 `int`，还包括其他值类型元素。查询参数生成器应先按数组实际元素类型读取，传给 `AppendValue(string, object, ...)` 前再对值类型装箱。

现在仅对零下界的一维数组使用索引快路：值类型用带元素类型的 `ldelem` 再 `box`，引用类型继续用 `ldelem.ref`。多维和非零下界数组交给已有的 `IEnumerable` 路径处理，避免把其下标规则误当成普通一维数组。元素类型只在生成委托时读取；每个元素的运行路径没有增加反射。

独立进程复测非空 `int[]` 已输出 `Values=1&Values=2`。回归矩阵分别直接编译 IL 和表达式策略，覆盖整数、长整数、十进制、可空值、枚举、字符串、对象、交错与多维数组、非零下界、空和 null 数组，并经过 HTTP GET 查询参数调用链。这里修复的是正确性；没有把它写成已测得的吞吐改进。

此策略仍使用 `DynamicMethod`，Native AOT 不支持这条动态生成路径。若需 AOT 查询对象，应另行定义静态映射或源码生成的入口，并在目标宿主上发布验证。
