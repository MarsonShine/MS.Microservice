# 从公开入口看 Core 的重复工作

同样一段代码，放在“每个元素”“每次请求”和“每个进程”这三个位置，代价可以差几个数量级。下面几篇笔记都从公开调用出发，把基线 `4ae3623` 中重复执行的工作和修改后的代码放在一起。

| 从哪里读 | 拿什么输入走一遍 |
| --- | --- |
| [链表遍历与文本规范化](performance-traversal.md) | 四节点链表怎样从十次逐层转发变为四次节点访问，再用 `Map` 取值。含中文标点和连续空白的标题怎样直接写入结果字符串 |
| [HTTP JSON 与摘要](performance-http-hash.md) | 两个客户端共享什么、哪些设置仍然独立。一个含中文和转义符的请求体经过哪些编码，32 字节摘要怎样输出成 64 个小写字符 |
| [规格组合、弱缓存与资源流](performance-query-cache.md) | 八个谓词为何曾创建七个单项字典。扫描 1024 个缓存项要读几次时钟，打开程序集资源后谁负责释放流 |
| 本文的文本差异示例 | 两段各三行的文本只有中间一行不同，哪些行需要变成新字符串 |

## TextDiff 为什么要晚一点创建行字符串

Lab 的文本比较入口调用 `TextDiff.Compare`。旧版一开始就拆完两份输入，然后才逐行寻找相同的开头与结尾。拆行时不仅保存边界，还立即切出了每一行的字符串。[调用方](../../../samples/Lab/MS.Microservice.Lab/Controller/TextToolsController.cs#L62) · [当前入口](../Text/TextDiff.cs#L25)

```diff
-var left = SplitLines(original);
-var right = SplitLines(updated);
+var leftLines = FindLines(original);
+var rightLines = ReferenceEquals(original, updated) ? leftLines : FindLines(updated);
```

以 `"head\r\nold\nlast"` 和 `"head\r\nnew\nlast"` 为例，下面是 `FindLines` 记录的区间。右端下标不计入区间，换行符计入所在行。

| 下标区间 | 原文本 | 新文本 | 最终输出 |
| --- | --- | --- | --- |
| `[0, 6)` | `head\r\n` | `head\r\n` | 一条 Unchanged |
| `[6, 10)` | `old\n` | `new\n` | 一条 Removed、一条 Added |
| `[10, 14)` | `last` | `last` | 一条 Unchanged |

旧版先切出六个行字符串，最后输出四条。新版用 `AsSpan(range).SequenceEqual(...)` 在原始字符上比较公共首尾，只在输出时切一次 `head\r\n` 和 `last`。左右两边都还有变化时，才把中间的行切出来，交给原有的最长公共子序列算法（LCS）。这个例子中，LCS 只收到 `old\n` 和 `new\n`。如果一边只多了几行，直接输出新增或删除记录即可。[首尾比较](../Text/TextDiff.cs#L29) · [构造变化区](../Text/TextDiff.cs#L46) · [保留换行符](../Text/TextDiff.cs#L137)

工作量上限仍按去掉公共首尾后的行数计算，重复行出现多个等长解时沿用原来的选择顺序。基准里，首尾各有 1024 行相同文本、只改中间一行时，每次少分配 **164,000 B**。两边各 96 行且完全不同时，可省的公共行消失了，耗时中位数约从 117.5 µs 到 119.3 µs，分配仍从 24,888 B 降到 23,008 B。读这项优化时，要同时看这两组输入。[现有重建与工作上限测试](../../../test/MS.Microservice.Core.Tests/Text/TextDiffTests.cs) · [全表和复测命令](../../../docs/validation/CORE-PERFORMANCE/summary.md)

> 差异一：内存分配
>
> `SplitLines` 每行都 `text[a..b]` **复制出一个新 string**，10 MB 文本会分配 30 MB 堆内存，而且这些字符串会活到调用方释放为止，单行超过 85 KB 还会直接进 LOH，触发大对象堆碎片。
>
> `FindLines` 只装 `Range`，这是一个 8 字节的 struct，`List<Range>` 底层是连续数组，缓存友好，扫描全程零字符串分配。
>
> 有意思的是“5 MB 单行”那行：`SplitLines` 也是 0 分配，因为 `text[0..text.Length]` 命中 `Substring` 返回原实例的优化。所以**分配差距只在“多行”时体现**，但扫描速度差距一直都在。
>
> 差异二：扫描速度（`IndexOfAny` 是向量化的）
>
> `text.AsSpan(start).IndexOfAny('\r', '\n')` 走的是 `SpanHelpers` 里 **SIMD 向量化**的实现，一次比较 16~32 个 char；而 `SplitLines` 是 C# 逐字符循环加边界检查。所以哪怕不分配（无换行场景），前者仍快 15 倍。
>
> 注意 `AsSpan(start)` 每轮都创建 span，但 span 是 ref struct，**不分配**，开销可忽略。
>
> 差异三：语义
>
> |                 | SplitLines                 | FindLines                               |
> | :-------------- | :------------------------- | --------------------------------------- |
> | 产物            | 行内容副本                 | 行在原文中的**坐标**                    |
> | 位置信息        | 丢失（除非调用方自己累计） | 保留，可做 offset→行号 二分映射         |
> | 随机访问第 n 行 | 直接索引                   | 直接索引后还需 `text[range]` 才拿到内容 |
> | 生命周期        | 字符串独立存在             | 依赖原文存活                            |
>
> **关键限制**：`Range` 拿不到零分配的 span。`text[range]` 对 string 走的是 `Substring`，**仍然分配字符串**。所以 FindLines 的“零分配”只有在消费端也只做位置计算、或自己 `text.AsSpan(r.Start.Value, r.End.Value - r.Start.Value)` 时才成立。如果下游反正要 `string`，你只是把分配推迟了一次，总量省不下那 30 MB。

## 全模块检查范围

盘点覆盖 `src/MS.Microservice.Core` 中的 116 个生产 C# 文件，排除生成目录。同样检查了没有落地改动的路径。下表按源码归属计数。

| 分组 | 文件数 | 审查结论 |
| --- | ---: | --- |
| Functional、Common、Extension、Microsoft | 44 | 改动集中在链表只读遍历、标题规范化、资源读取。保留不可变结构、`Match`、`Map`、`Bind`、`Option` 和左折叠。树变换、重试及重排各有自己的求值或排序契约，链表结果不能外推到这些操作 |
| Domain、Dto、Specification、Reflection、Linq、Concurrent、Memory、EventBus、FeatureManager | 40 | 去掉单参数表达式组合的映射字典，简化弱缓存扫描。反射访问器已有委托缓存，特性开关依赖可更新配置，仓储和 DTO 多为契约。事件订阅还涉及并发可见性，未变更其行为 |
| Net、Http、IO、Serialization、Security、Identity、Auditing、Messaging | 27 | 优化客户端构造、UTF-8 请求体及摘要格式化。文件与 ZIP 入口的限额、链接校验、失败清理及加密认证属于正确性边界。没有用内存 HTTP 基准推算磁盘、网络或加解密吞吐 |
| Text 与根目录的 Check、CorePlatformException、StringBuilderCache | 5 | 延后差异比较中的行物化。编码转换按块处理并涉及 BOM、回退及流所有权，StringBuilder 缓存已有线程隔离和容量上限，通用检查及异常类型未见同类重复工作 |

这份清单记录源码覆盖；实际运行的性能场景及范围见[验收记录](../../../docs/validation/CORE-PERFORMANCE/summary.md)。
