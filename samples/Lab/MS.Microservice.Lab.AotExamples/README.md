# 性能与 AOT 写法对照

独立学习类库，不打包、不启动 Web，也不被生产组件引用。
`Legacy` 保留旧机制，`Static` 独立实现替代写法。按主题添加示例。

## 验证洗牌

| 实现 | 行为与成本 |
|---|---|
| `Legacy/ValidatedShuffleExample.cs` | 来自 `7b1c78a`。每轮丢弃三个洗牌副本；修复固定位置时随机寻找候选，全部相同的输入会无限循环。 |
| `Static/ValidatedShuffleExample.cs` | 使用官方 `Random.Shared.Shuffle(CollectionsMarshal.AsSpan(list))` 原地洗牌；轮换多个固定位置，单个固定位置有限扫描候选。最多十轮随机尝试，修复过程最多两次线性扫描。 |

生产入口仍为 `list.ValidatedShuffle()`，调用方无需迁移。
至少两个元素且值互不相等时保证全部错位；重复值尽力错位，始终保留元素及其数量并有限结束。
这里不保证随机排列的均匀分布。公开 `Shuffle` 仍返回新列表，不修改输入。
`AsSpan` 不复制列表；使用期间不增删元素。普通随机洗牌允许元素留在原位，因此仍需错位修复。

```csharp
var values = new List<int> { 1, 2, 3, 4 };
MS.Microservice.Lab.AotExamples.Static.ValidatedShuffleExample.ValidatedShuffle(values);
```

运行示例对照：

```powershell
dotnet test test/MS.Microservice.Lab.AotExamples.Tests
```

测试用带比较次数上限的值对象复现旧缺陷；不要直接用普通全相同列表运行旧实现。
随机输出按元素多重集合和错位契约比较，不比较具体排列。

该主题属于运行时性能修复，旧算法本身没有反射或动态代码生成问题。
示例的单元测试不构成 NativeAOT 发布验证。
