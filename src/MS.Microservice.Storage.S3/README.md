# S3 文件存储

可选适配器，实现 Core 的 `IFileStorage`，提供条件新建、流式下载、元数据、删除及限时 GET URL。仅引用此项目的应用引入 AWS SDK；本地存储和 HTTP 上传组件不需要它。

## 使用

宿主提供已配置凭据和端点的 `IAmazonS3`，并预先创建私有暂存目录：

```csharp
using MS.Microservice.Core.IO;
using MS.Microservice.Storage.S3;

var s3 = new S3FileStorage(client, new S3FileStorageOptions
{
    BucketName = "my-bucket",
    Prefix = "tools/media",
    TemporaryDirectory = privateStagingDirectory,
    MaximumUploadBytes = 64L * 1024 * 1024
});
IFileStorage storage = s3;
var key = StoredFileKey.Create(".png");
await storage.WriteNewAsync(key, input, cancellationToken);
var metadata = await storage.GetMetadataAsync(key, cancellationToken);
await using var download = await storage.OpenReadAsync(key, cancellationToken);
await download.CopyToAsync(output, cancellationToken);
```

宿主完成 key 的访问授权后，可调用 `s3.CreateReadUrlAsync(key, TimeSpan.FromMinutes(10), cancellationToken)`。URL 是 bearer 凭据，不应向未授权用户分发；临时凭据到期会缩短实际有效期。

## 参数与行为

| 参数／入口 | 默认／要求 |
| --- | --- |
| `client` | 宿主配置并释放；适配器不创建桶或改变 ACL |
| `BucketName` | 必填可信配置 |
| `Prefix` | 默认空；ASCII 安全相对段，无首尾斜杠、空段或点路径 |
| `TemporaryDirectory` | 已存在、可信的私有目录；Windows 权限继承父目录 |
| `MaximumUploadBytes` | 默认 64 MiB；允许 0 至 5 GiB，0 仅接受空对象 |
| `key` | 由 `StoredFileKey.Create` 创建；不接受路径／大小写别名 |
| 输入流 | 从当前位置读取，支持不可定位流，保持打开 |
| 下载流 | 调用方释放，释放时关闭完整 HTTP 响应；不套用上传预算 |
| 元数据／删除 | 对象缺失返回 null／幂等成功；删除仅针对当前 key |
| GET URL 有效期 | 1 秒至 7 天；签名过程仅在调用前后检查取消 |

写入不覆盖已有对象。上传需要暂存磁盘空间，不实现 multipart；原始输入类型及大小之外的业务检查仍由宿主承担。403 和可识别错误桶不会当作缺失对象；HEAD 通用 404 无法可靠区分两者，部署时应确认桶和权限。

**写入异常不能证明远端没有完成。** 先持有 key，发生网络／取消／清理错误后再查询协调；组件不自动回滚远程对象。暂存清理再次失败会保留原错误，可能留下本次文件。版本桶的历史与保留策略由宿主管理。

依赖 AWSSDK.S3 4.0.104.2 及 AWSSDK.Core。兼容端点必须支持条件新建；真实 AWS／MinIO 环境尚未验证，接入时需在目标环境确认。实现、错误语义和验证边界见[维护设计](docs/s3-file-storage-design.md)。
