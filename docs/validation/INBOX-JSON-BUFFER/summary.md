# 收件箱 JSON 读取的缓冲区与异常验收

2026 年 11 月 23 日，以 `afd966d` 中的 Notifications 实现为基线。本次检查共用字节数组读取通知及其回执时，解析得到的值是否独立、预算是否按每份文件实际字节数累计，以及损坏数据会报告哪一种异常。[源码前后对照与设计取舍](../../../src/MS.Microservice.Notifications/docs/notification-performance.md#为什么要在读取方法内把字节变成对象)

- 通知为空文件和合法 JSON `null` 的用例在旧实现上复现了错误文案。新版对空文件抛出 `JsonException`，对 JSON `null` 抛出带有 `The stored notification contains JSON null.` 文案的 `InvalidDataException`。两种内容写入回执后，经 `ListAsync` 和 `MarkReadAsync` 读取均抛出 `JsonException`。[异常用例](../../../test/MS.Microservice.Notifications.Tests/Inbox/LocalNotificationInboxTests.cs#L199)
- 一份长中文正文、一份短正文和已读回执共用缓冲区，扫描预算等于实际文件字节数之和时，两份通知的完整内容、排序、回执时间和未读计数均正确；预算减少一字节就拒绝返回结果。[精确预算用例](../../../test/MS.Microservice.Notifications.Tests/Inbox/LocalNotificationInboxTests.cs#L120)
- `dotnet test test/MS.Microservice.Notifications.Tests -c Release --no-restore` 通过 **85 项**，失败 0 项，跳过 0 项。

## 修改前后的公开方法基准

Windows x64，.NET 11 Release，`DOTNET_TieredCompilation=0`。128 封通知各含 2048 个 ASCII 正文字节。相同基准程序按旧新、新旧、旧新交错启动六个进程。每场景预热一批再测七批，每批执行列表 2 次或重复标记 16 次，校验整页输出或回执时间。下表取进程内中位数，再对每个版本的三个进程取中位数。B/次为进程内每次调用的托管分配。[基准源码](../../../benchmarks/MS.Microservice.Notifications.Benchmarks/Program.cs)

| 场景 | 旧 ms/次 | 新 ms/次 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 全未读，取首页 8 封 | 89.817 | 87.039 | 817208 | 815160 |
| 半数已读，取首页 8 封 | 126.690 | 125.308 | 885816 | 884280 |
| 半数已读，跳过 120 封取 8 封 | 122.737 | 122.745 | 889676 | 888216 |
| 半数已读，取全部 128 封 | 124.433 | 126.703 | 890636 | 889176 |
| 重复标记已读 | 0.705 | 0.722 | 3612 | 3639 |

列表每次少分配约 1.4–2 KiB，重复标记多分配 27 B。半数已读首页的三个进程中位数在旧版为 120.257–148.727 ms，新版为 124.177–126.453 ms。各场景的耗时变化未呈现一致的加速趋势。
