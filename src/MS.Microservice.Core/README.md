# Core 通用工具

提供文件存储、摘要与 ZIP、文本与 JSON、函数式结果、集合、规格及 HTTP 辅助。引用本项目会同时引用 Domain.Primitives；不需要具体业务模型、数据库或 Lab 宿主。

| 能力 | 主要入口 | 实现与边界 |
| --- | --- | --- |
| 本地文件生命周期 | `IFileStorage`、`LocalFileStorage`、`StoredFileKey` | [存储设计](docs/local-file-storage-design.md) |
| 文件摘要与重复候选 | `FileHashHelper` | [摘要设计](docs/file-hashing-design.md) |
| ZIP 压缩与解压 | `ZipArchiveHelper` | [归档设计](docs/zip-archives-design.md) |
| 文本差异、编码、JSON | `TextDiff`、`TextEncodingConverter`、`JsonText` | [文本工具设计](docs/text-tools-design.md) |
| HTTP 调用与日志 | `LogHttpClient`、`LoggingHttpClientHandler` | [正文日志行为](Net/Http/body-logging.md) |
| 函数式结果与查询规格 | `Functional`、`Specification` | 公共类型注释；数据库执行由持久化组件提供 |
| 业务缓存治理 | 独立的 [Caching](../MS.Microservice.Caching/README.md) 组件 | Core 不再提供分布式缓存 JSON 扩展或缓存配置；弱引用工具在 `Memory` 中 |
| 审计操作上下文 | `AuditOperationContext`、`AuditOperationAttribute` | [EF 自动采集接入](../../MS.Microservice.Persistence/src/MS.Microservice.Persistence.EFCore/README.md#automatic-business-auditing)；Core 仅定义操作身份，不访问数据库 |

## 敏感字段

敏感字段使用 `MS.Microservice.Core.Security.SensitiveData`：在 DTO 属性上声明 `[Sensitive(SensitiveKind.Phone)]`、`Email` 或 `Secret`。手机号／邮箱掩码，Secret 从受保护输出中排除。规则不改变业务实体或缓存对象。

```csharp
var protectedFields = SensitiveValues.Project(dto); // 日志／导出的扁平字段
logger.LogSensitive(LogLevel.Information, "profile.changed", dto);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.UseSensitiveValues();
var json = JsonSerializer.Serialize(dto, jsonOptions);
```

标记须配合这些入口或 EF 审计适配器使用。普通字符串日志、其他 JSON 配置、未接入的导出不会自动保护；嵌套对象不由 `Project` 展开。Phone 识别 8–15 位数字，可带前导 `+`；Email 识别普通本地部分与 ASCII 域名，未识别格式完整遮盖，空值保留为空。JSON 只影响写出，读入保留原值。投影复用运行时编译访问器，JSON 分类在构建类型元数据时解析；这些反射适配路径尚不支持 Native AOT／裁剪，纯标量掩码无此依赖。完整策略和限制见 [敏感字段设计](docs/sensitive-data-design.md)。


## 文件存储

```csharp
using MS.Microservice.Core.IO;

IFileStorage storage = new LocalFileStorage(storageDirectory);
var key = StoredFileKey.Create(".png");
await storage.WriteNewAsync(key, input, cancellationToken);
var metadata = await storage.GetMetadataAsync(key, cancellationToken);

await using (var saved = await storage.OpenReadAsync(key, cancellationToken))
    await saved.CopyToAsync(output, cancellationToken);

await storage.DeleteAsync(key, cancellationToken);
```

只需保存并获得新 key 时，可用 `new LocalFileStorage(storageDirectory).SaveAsync(input, ".png", cancellationToken)`。

| 参数／结果 | 规则 |
| --- | --- |
| `storageDirectory` | 可信、稳定、由宿主管理的目录；按需创建 |
| 扩展名 | 点加 1–9 个 ASCII 字母或数字；输出转为小写 |
| `key` | `StoredFileKey.Create` 生成的规范 key；不能传路径或大小写别名 |
| 输入流 | 从当前位置读取，不复位、不关闭 |
| 下载流 | 调用者释放；缺失抛出 `FileNotFoundException` |
| 元数据／删除 | 缺失元数据返回 `null`；删除缺失文件成功 |

写入不覆盖已有 key，失败不会发布部分文件。清理再次失败可能留下暂存文件，并以 `AggregateException` 保留原错误。取消不撤销已完成的发布或删除。本组件不校验上传类型／大小，也不负责用户权限、文件记录或过期清理；key 本身不是授权证明。

## 文件摘要与重复候选

```csharp
var sha256 = await FileHashHelper.ComputeSha256Async(filePath, cancellationToken);
var groups = await FileHashHelper.FindDuplicatesAsync(filePaths, cancellationToken);
```

摘要是 64 个大写十六进制字符。流重载读取当前位置至末尾并保持流打开。`filePaths` 是明确提供的稳定文件路径；结果按长度和 SHA-256 分组，不遍历目录、不自动删除，也不进行额外逐字节比较。检测到文件变化会失败，但这些检查不提供文件系统快照。

## ZIP

```csharp
await using (var destination = new FileStream("export.zip", FileMode.CreateNew, FileAccess.Write))
    await ZipArchiveHelper.CreateZipAsync("data", destination,
        cancellationToken: cancellationToken);

await using var archive = File.OpenRead("export.zip");
await ZipArchiveHelper.ExtractZipAsync(archive, "extracted",
    new ZipExtractionOptions { MaxEntries = 10_000 }, cancellationToken);
```

| 参数 | 默认／要求 |
| --- | --- |
| 压缩源目录 | 保留内部目录和空目录；输出必须在源目录外，不跟随链接 |
| 解压输入 | 可读、可定位；调用者释放 |
| 解压目标 | 必须不存在，父目录必须已存在；不覆盖 |
| `MaxEntries` | 10,000；允许零 |
| `MaxEntryBytes` | 512 MiB，按实际读取量检查 |
| `MaxTotalBytes` | 1 GiB，按实际读取量检查 |

路径、重复／冲突条目、链接和内容校验失败不会发布解压目录。调用者仍需限制归档文件大小并保持目录可信；CRC 不能证明来源真实性。压缩失败可能留下部分输出流，调用者应丢弃。只恢复内容和目录，不恢复权限、时间戳或链接。

## 文本与 JSON

```csharp
using System.Text;
using MS.Microservice.Core.Text;
using MS.Microservice.Core.Serialization;

var changes = TextDiff.Compare(original, updated);
await TextEncodingConverter.ConvertAsync(input, output,
    new UTF8Encoding(true, true),
    new UnicodeEncoding(false, false, true), cancellationToken);
var prettyJson = JsonText.Format(json);
var compactJson = JsonText.Format(json, indented: false);
```

| 入口／参数 | 行为与限制 |
| --- | --- |
| `TextDiff.Compare` | 精确比较行，保留空白及换行符；替换表示为删除和新增 |
| `maximumComparisonWork` | 默认 4,000,000，必须为正；不是输入长度上限 |
| 两个 `Encoding` | 明确决定编码、BOM 及回退策略，不自动检测；严格编码会拒绝无效字符 |
| 编码转换流 | 从当前位置处理并保持打开；不得原地转换；失败可能有部分输出 |
| `JsonText.Format` | 一个完整 JSON 值；不使用业务序列化策略；非法 JSON 抛出 `JsonException` |

JSON 排版和文本差异保留输入与结果在内存中，宿主应限制输入大小。编码转换采用缓冲处理；源编码匹配的 preamble 会消费，目标 BOM 按目标编码和输出位置决定。规范化标题与字段脱敏分别使用已有 `Common.TextNormalizer`、`Security.SecretField`，这些工具不自动替换它们。

## HTTP 与加密

`LogHttpClient.GetAsync` 支持属性对象或字典查询参数，省略 null、保留空字符串、展开集合；POST 使用 UTF-8 JSON。取消、状态错误和 JSON 错误分别保留原异常类型。每次请求的头不改动共享默认头。

`LoggingHttpClientHandler` 开启 Information 日志时默认读取完整 URI 和正文。只需要元数据日志时，显式配置：

```csharp
using MS.Microservice.Core.Net.Http;

services.Configure<LoggingHttpClientHandlerOptions>(o => o.EnableRedaction = true);
services.AddTransient<LoggingHttpClientHandler>();
services.AddHttpClient("remote-api").AddHttpMessageHandler<LoggingHttpClientHandler>();
```

该模式仍记录不含查询串的路径，路径也应避免放凭据。日志读取与缓冲行为见[实现说明](Net/Http/body-logging.md)。

| 加密入口 | 使用限制 |
| --- | --- |
| [AES-256-GCM](Security/Cryptology/authenticated-encryption.md) | 调用者提供 32 字节密钥；旧 AES-ECB／3DES 已停用 |
| [RSA](Security/Cryptology/rsa-oaep.md) | 新密文使用 OAEP-SHA256 和版本前缀；保留无前缀旧密文读取 |
| `HmacSha256` | 显式提供密钥；普通 SHA-256 摘要不替代消息认证 |

依赖与复制方式见[组件消费](../../docs/components/consumption.md)，JSON 对象序列化配置见[配置说明](Serialization/json-configuration-reuse.md)。
