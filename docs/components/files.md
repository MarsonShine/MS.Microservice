# Files 常用流程

文件操作复用 Core 的 `IFileStorage`，本地使用 `LocalFileStorage`，对象存储使用可选的 [S3FileStorage](../../src/MS.Microservice.Storage.S3/README.md)。不需要数据库或独立的 Files 服务。

## 类库接入

```csharp
using MS.Microservice.Core.IO;

IFileStorage storage = new LocalFileStorage(privateDirectory);
var key = StoredFileKey.Create(".txt");
await storage.WriteNewAsync(key, input, cancellationToken);
var metadata = await storage.GetMetadataAsync(key, cancellationToken);
await using (var content = await storage.OpenReadAsync(key, cancellationToken))
    await content.CopyToAsync(destination, cancellationToken);
await storage.DeleteAsync(key, cancellationToken);
```

将 `storage` 换成已配置的 S3FileStorage 即可复用这组操作。创建后保留 key 再执行写入，S3 网络错误时可用它核对远端状态。输入流从当前位置读取并保持打开，返回的下载流由调用方释放。

文件名、大小等信息可以直接返回给业务调用方；如果应用需要长期保存原始名称或附件归属，由应用保存。存储 Key 不是访问凭证，宿主在调用前完成授权，并对上传大小和需要的文件格式进行校验。

## Lab HTTP 流程

登录 Lab 后，通过 Swagger 依次调用：

1. `POST /api/lab/tools/files`：multipart 字段 `File` 上传文件，返回 `key`、`originalFileName`、`length` 和 `downloadUrl`。
2. `GET /api/lab/tools/files/{key}`：查询当前用户文件的大小和最后修改时间。
3. `GET /api/lab/tools/files/{key}/content`：下载原始内容，默认附件名称为 key。
4. `DELETE /api/lab/tools/files/{key}`：删除文件，重复删除也成功。

`originalFileName` 去掉客户端目录，仅在上传结果中返回，后续元数据查询不恢复原始名称。需要按原名保存下载内容时，由客户端使用先前保存的名称。

上传接受非空文件，实际大小最多 20 MiB；通用文件入口按字节保存，不验证业务格式。需要图片或 Excel 格式校验时使用现有 `FileUploadValidator`。本地下载打开文件后检查 64 MiB 上限，随后交给 ASP.NET Core 输出并释放流。文件按登录用户隔离，其他用户查询或下载返回 404，删除不会影响原用户文件。

S3 示例在 `/api/lab/tools/s3`，先配置桶与凭据，然后持有生成的 key，使用 `PUT /{key}` 上传；查询、下载、删除分别使用 `GET /{key}`、`GET /{key}/content`、`DELETE /{key}`。上传同样返回原始叶子文件名。S3 下载沿用有大小上限的缓冲读取，使远端响应和客户端在控制器返回前完成释放。
