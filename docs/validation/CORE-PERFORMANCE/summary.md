# Core 性能检查验收

2026-11-20，Windows 10 x64、Intel Core i5-9400、SDK 11.0.100、.NET 11.0.0，Release。基线为 `4ae3623`，初始工作区对 `AGENTS.md` 的配图指令不影响程序集。表中“新”对应保留不可变链表、`Map`、`Option` 和左折叠组合的最终实现。[116 个生产文件的审查范围与源码导读](../../../src/MS.Microservice.Core/docs/performance-review.md)

## 公开入口的同机对照

| 场景 | 耗时 ns/次（旧 → 新） | 托管分配 B/次（旧 → 新） |
| --- | ---: | ---: |
| 链表枚举，8 项 | 389.5 → 187.8 | 432 → 104 |
| 链表 ForEach，8 项 | 167.5 → 149.9 | 584 → 208 |
| 链表枚举，1024 项 | 3,090,400 → 12,450 | 49,200 → 104 |
| 链表 ForEach，1024 项 | 19,672.3 → 11,194.1 | 57,480 → 208 |
| 规范化，短 ASCII 原样 | 212.9 → 12.3 | 0 → 0 |
| 规范化，中英文标点与空白 | 1,138.8 → 301.6 | 704 → 64 |
| 规范化，4096 个 ASCII 字符原样 | 8,267.6 → 388.7 | 0 → 0 |
| 规范化，4096 个汉字原样 | 81,741.8 → 26,455.5 | 0 → 0 |
| Specification 追加八个谓词 | 6,907 → 3,973.4 | 6,112 → 2,976 |
| 弱缓存扫描 1024 个活项 | 32,739.8 → 5,432 | 32 → 0 |
| 读取 ChineseEncoder | 39,959.4 → 3.1 | 17,712 → 0 |
| 构造 LogHttpClient | 84,398.4 → 142.2 | 17,945.6 → 224 |
| POST 24 B JSON | 6,205.3 → 5,691.8 | 1,872 → 1,592 |
| POST 81,934 B JSON | 507,090.6 → 246,706.2 | 214,882 → 83,504 |
| 字符串 SHA-256 | 1,864.7 → 591.2 | 1,808.1 → 192 |
| 差异，公共首尾各 1024 行 | 138,402.5 → 111,393.8 | 525,336 → 361,336 |
| 差异，两边各 96 行且完全不同 | 117,526.7 → 119,334 | 24,888 → 23,008 |

TextDiff 的完全不同输入耗时约高 1.5%，分配降低约 7.6%；原样文本的托管分配原本就是 0。链表数据仅涉及只读枚举和执行回调，两者均按输入顺序读完。没有把这些结果推广到建树、异步调度、在线网络时延、GC 暂停或进程峰值内存。

## 怎样复测

在仓库根目录执行以下命令。设置环境变量是为了让两个版本都测优化后的 JIT 代码，便于减少短场景受分层编译切换的影响。

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/MS.Microservice.Core.Benchmarks -c Release
dotnet run --project benchmarks/MS.Microservice.Core.Benchmarks -c Release -- --resource-smoke
dotnet test test/MS.Microservice.Core.Tests -c Release
```

前后比较使用同一份已编译基准：保存基线构建的 `MS.Microservice.Core.dll`，在临时基准输出目录中替换该 DLL，按旧、新、新、旧、旧、新顺序分别启动独立进程。每个场景先预热一批，随后测七批。耗时和进程托管分配分别取每批每次调用的中位数，表中再对同版本三个进程取中位数。初始化输入及各批最后一次结果的契约校验都在计量窗口外。资源读取旧版返回已释放的流，不纳入数值对比。[计量实现](../../../benchmarks/MS.Microservice.Core.Benchmarks/Program.cs)

链表在计时前建好，查询表达式在计时后编译。弱缓存预装 1024 个未到期的活条目。POST 使用同步完成的内存 handler，读出全部正文并返回 JSON 整数 1，Information 日志关闭。除直接生成 UTF-8 字节外，新版也在日志关闭时省去组装日志参数的开销；表中是组合效果。构造客户端的数字包含选项对象，编码器 getter 为预热后读取，不含首次建表。[集合输入](../../../benchmarks/MS.Microservice.Core.Benchmarks/CollectionCases.cs) · [规格与缓存输入](../../../benchmarks/MS.Microservice.Core.Benchmarks/QueryCases.cs) · [HTTP 与摘要输入](../../../benchmarks/MS.Microservice.Core.Benchmarks/InfrastructureCases.cs)

## 行为验收

HTTP 日志封装提交 `9296c7e` 将三个事件的级别判断移入内部扩展方法。此前表格的耗时来自封装前的三组对照。封装后单独复跑全部 17 个场景，24 B 和 81,934 B POST 的分配仍分别为 1592 B/次和 83,504 B/次。资源流烟测通过，完整 Core 测试通过 1131 项，失败 0 项、跳过 0 项。

- `dotnet test test/MS.Microservice.Core.Tests -c Release` 通过 1129 项，失败 0 项，跳过 0 项。覆盖不可变链表尾部共享、独立枚举器、提前停止、异常传播、非交换左折叠及空链种子，也覆盖 HTTP 正文、内容类型和长度、配置隔离、哈希、缓存回收和谓词参数绑定。既有测试覆盖转义、取消、状态错误及文本差异工作量边界。
- 用基线和新版程序集对全部 65,536 个 UTF-16 码元运行首尾、重复与中间位置输入，含 null 和空字符串，共 196,610 组 `TextNormalizer.Normalize` 的序数比较结果一致。
- 在独立算法对照中，把基线和新 `TextDiff` 源码编译到不同命名空间，比较 23,826 对输入的完整 Kind/Text 序列或异常类型与参数名。覆盖重复行多个等长解、混合换行、未配对代理项、公共首尾与工作量边界，结果相同。这一验证独立于上表的耗时测量。
- 同一个资源读取烟测在基线 DLL 上于首次读取抛出 `ObjectDisposedException`。新版可从头读取完整资源，两次打开的游标独立，缺失资源在等待 `ValueTask` 时抛出含资源名的 `ArgumentException`。返回的资源流由调用者释放。
