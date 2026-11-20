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

## 全模块检查范围

盘点覆盖 `src/MS.Microservice.Core` 中的 116 个生产 C# 文件，排除生成目录。同样检查了没有落地改动的路径。下表按源码归属计数。

| 分组 | 文件数 | 审查结论 |
| --- | ---: | --- |
| Functional、Common、Extension、Microsoft | 44 | 改动集中在链表只读遍历、标题规范化、资源读取。保留不可变结构、`Match`、`Map`、`Bind`、`Option` 和左折叠。树变换、重试及重排各有自己的求值或排序契约，链表结果不能外推到这些操作 |
| Domain、Dto、Specification、Reflection、Linq、Concurrent、Memory、EventBus、FeatureManager | 40 | 去掉单参数表达式组合的映射字典，简化弱缓存扫描。反射访问器已有委托缓存，特性开关依赖可更新配置，仓储和 DTO 多为契约。事件订阅还涉及并发可见性，未变更其行为 |
| Net、Http、IO、Serialization、Security、Identity、Auditing、Messaging | 27 | 优化客户端构造、UTF-8 请求体及摘要格式化。文件与 ZIP 入口的限额、链接校验、失败清理及加密认证属于正确性边界。没有用内存 HTTP 基准推算磁盘、网络或加解密吞吐 |
| Text 与根目录的 Check、CorePlatformException、StringBuilderCache | 5 | 延后差异比较中的行物化。编码转换按块处理并涉及 BOM、回退及流所有权，StringBuilder 缓存已有线程隔离和容量上限，通用检查及异常类型未见同类重复工作 |

这份清单记录源码覆盖；实际运行的性能场景及范围见[验收记录](../../../docs/validation/CORE-PERFORMANCE/summary.md)。
