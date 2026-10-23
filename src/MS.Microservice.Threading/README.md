# BatchProcessing
仅依赖 .NET，复用 Parallel.ForEachAsync。CollectAsync 接受稳定的有限 IReadOnlyList、异步处理函数和显式正整数 maxConcurrency，返回按输入索引排序的逐项结果（Index、Value、Error、IsSuccess）。

```csharp
var results = await BatchProcessing.CollectAsync<string, string>(paths,
    async (path, token) => await File.ReadAllTextAsync(path, token),
    maxConcurrency: 4, cancellationToken);
```

单项异常记入结果并继续；调用方取消停止派发，将令牌传入在途处理，等待在途处理结束后抛出取消，不返回不完整列表。与整体取消无关的单项取消记录为该项失败。处理函数必须并发安全；执行期间不得修改输入列表。处理函数忽略取消时，本方法必须等待它结束。

结果保存在内存，适合有限批次；无持久化、重试、队列或事务。遇错停止的简单场景可直接使用 Parallel.ForEachAsync。
