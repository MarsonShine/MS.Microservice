# 本地持久化站内通知

`LocalNotificationInbox` 提供按 owner 分隔的站内通知发布、倒序分页、未读数和幂等已读回执。通知和已读时间保存到宿主管理的目录，创建新的组件实例或重启进程后继续可读；没有依赖内存列表保存业务状态。

```csharp
var inbox = new LocalNotificationInbox(configuredPrivateDirectory);
var message = await inbox.PublishAsync(userId, "文件处理完成", "输出已生成。", "/tools/jobs/123", token);
var page = await inbox.ListAsync(userId, take: 20, cancellationToken: token);
await inbox.MarkReadAsync(userId, message.Id, token);
```

调用方负责从已验证的登录身份获取 owner，并检查接口权限；owner 目录是其精确 UTF-8 标识的 SHA-256，不执行大小写、空白或 Unicode 合并。目录散列和随机通知 Id 都不是授权凭据，也不提供内容加密。正文是纯文本，ActionPath 是应用内以 `/` 开始的路径；组件不渲染 HTML 或执行链接。

通知 JSON 为不可变文件，已读状态保存为另一份仅创建一次的 UTC 时间回执。两者复用 Core `LocalFileStorage.WriteNewAsync` 的完整写入与不覆盖发布。同一通知并发标已读保留第一个成功发布的回执，不改写通知正文或创建时间。写入期间的随机点前缀暂存文件不参与查询。目录须可信且稳定，由应用管理；不支持恶意本机进程替换文件，也不提供跨用户业务事务。

分页按 `CreatedAtUtc` 和 Id 倒序，默认 `skip=0,take=50`，默认最大一页 100 条。每次查询扫描该 owner 的所有当前通知和回执，并计算准确的本次总数／未读数。查询与并发发布、已读不是数据库快照，使用 offset 翻页时并发插入可能令下一页重复或跳过，应按 Id 去重或重新加载。输入目录未创建时返回空页，不创建目录。

默认单份 JSON 上限 64 KiB，单次扫描最多 10,000 条通知和 32 MiB 实际编码字节，超限明确失败，不返回被截断的计数；已读回执也计入扫描字节。发布受单份预算约束，查询受累计扫描预算约束。宿主负责历史通知保留期和离线清理；没有后台自动删除。持续大量通知或多进程事务需求应使用宿主自己的数据库实现，此入口是本地文件后端。

方法读取调用方取消并沿用 Core 的原子写入规则。取消在发布之后发生不撤销已保存内容，调用方不得据取消盲目重复发布。读取缺失通知或为另一个 owner 标已读会失败；损坏数据会报告错误，不静默当作未读或空通知。SMTP 接口继续由原有实现提供，本组件通过项目引用复用 Core 文件写入，不引入新的服务项目。
