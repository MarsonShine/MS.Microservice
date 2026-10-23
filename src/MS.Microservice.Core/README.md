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
| 同键并发请求合并 | `Concurrent.SingleflightManager` | 兼容入口转发到 [Threading](../MS.Microservice.Threading/README.md#singleflight)，新接入可直接引用 Threading |
| 业务缓存治理 | 独立的 [Caching](../MS.Microservice.Caching/README.md) 组件 | Core 不再提供分布式缓存 JSON 扩展或缓存配置；弱引用工具在 `Memory` 中 |
| 敏感字段输出 | `SensitiveValues`、`SensitiveJson`、`LogSensitive` | [注册、扫描与执行链](docs/sensitive-data-design.md) |
| 审计操作上下文 | `AuditOperationContext`、`AuditOperationAttribute` | [EF 自动采集接入](../../MS.Microservice.Persistence/src/MS.Microservice.Persistence.EFCore/README.md#automatic-business-auditing)；Core 仅定义操作身份，不访问数据库 |

## 敏感字段

在 DTO 属性上声明 `[Sensitive(SensitiveKind.Phone)]`、`Email` 或 `Secret`，再接入需要保护的输出入口。手机号／邮箱输出掩码，Secret 从受保护输出中排除；业务实体或缓存对象中的原值保持不变。

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Security.SensitiveData;

var dto = new ProfileView { Phone = "13800138000", Email = "alice@example.com", InternalNote = "仅供演示" };
var protectedFields = SensitiveValues.Project(dto); // 日志／导出的扁平字段
logger.LogSensitive(LogLevel.Information, "profile.changed", dto);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.UseSensitiveValues();
var json = JsonSerializer.Serialize(dto, jsonOptions);

public sealed class ProfileView
{
    [Sensitive(SensitiveKind.Phone)] public string? Phone { get; set; }
    [Sensitive(SensitiveKind.Email)] public string? Email { get; set; }
    [Sensitive(SensitiveKind.Secret)] public string? InternalNote { get; set; }
}
```

| 使用场景 | 接入及结果 |
| --- | --- |
| 标量处理 | `SensitiveValues.Protect("13800138000", SensitiveKind.Phone)` 返回 `138****8000`；Email 示例返回 `a***@example.com` |
| JSON／HTTP 响应 | 对相应 JsonSerializerOptions 调用 `UseSensitiveValues()`；上例输出 Phone／Email 掩码，没有 InternalNote，反序列化仍接收原值 |
| 结构化日志 | `LogSensitive` 在提交 ILogger 前创建受保护状态，任意后端接收相同安全字段；关闭日志级别时不读取 DTO |
| Excel／CSV 等导出 | 将 `Project` 返回的扁平字段传给导出器，而不是直接传原 DTO；Secret 列不存在，源对象保持不变 |
| EF 自动审计 | 在已选入 `AuditChanges` 的实体属性上声明分类；Phone／Email 保存掩码，Secret 只记变更事实，见 [接入说明](../../MS.Microservice.Persistence/src/MS.Microservice.Persistence.EFCore/README.md#automatic-business-auditing) |

ASP.NET Core MVC 响应在注册控制器时接入：

```csharp
using MS.Microservice.Core.Security.SensitiveData;

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UseSensitiveValues();
    });
```

接入后可直接 `return Ok(dto)`，响应按 DTO 属性上的标记处理。查看、修改与受保护导出可按 [Lab 示例](../../samples/Lab/MS.Microservice.Lab/README.md#敏感字段示例) 体验。

使用时需注意：

- `[Sensitive]` 只声明分类，各输出路径需分别接入。普通字符串日志、其他 JSON 配置和直接导出原 DTO 都不会自动保护；实体映射为 DTO 时，标记不会自动传递，DTO 也须声明。
- Phone／Email 必须用于 `string` 属性。Phone 识别 8–15 位数字，可带前导 `+`；Email 识别普通本地部分与 ASCII 域名。未识别格式完整遮盖，`null` 和空字符串保持原样。
- `Project` 只保留扁平标量字段，嵌套对象被省略；JSON 保护只影响写出，反序列化仍接收原值。需要完整值访问时，由宿主提供独立授权的 DTO／入口。
- JSON 和对象投影适配尚不支持 Native AOT／裁剪；直接调用标量 `Protect` 无此限制。

各入口的注册位置、标记扫描时机、值处理流程及 Lab 代码入口见 [敏感字段的注册、扫描与执行链](docs/sensitive-data-design.md)。


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
