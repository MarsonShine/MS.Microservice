# ZIP 压缩与解压

`ZipArchiveHelper` 位于现有 Core 的 `IO` 命名空间，只使用 .NET 自带的 ZIP 实现。它处理普通文件和目录，不负责上传、文件保留策略、任务调度或其他归档格式。

```csharp
await using var zipOutput = File.Create("export.zip"); // 放在源目录之外
await ZipArchiveHelper.CreateZipAsync("export", zipOutput, cancellationToken: cancellationToken);

await using var zipInput = File.OpenRead("export.zip");
await ZipArchiveHelper.ExtractZipAsync(zipInput, "extracted", new ZipExtractionOptions
{
    MaxEntries = 10_000,
    MaxEntryBytes = 512L * 1024 * 1024,
    MaxTotalBytes = 1024L * 1024 * 1024
}, cancellationToken);
```

压缩保存源目录内部的相对目录结构，包括空目录；根目录本身不额外包一层。所有调用者提供的流都保持打开。压缩失败或取消时，输出流可能已经写入部分 ZIP，调用者应删除或丢弃该输出。直接传入的输出 `FileStream` 如果位于源目录内会被拒绝；包装文件流时，调用者同样必须保证实际输出在源目录之外。

解压输入必须可读且可定位。网络或上传流需要先写入调用者管理的临时文件，避免 .NET 对不可定位的 ZIP 输入进行全量内存缓冲。内容按 80 KiB 缓冲区逐块处理；ZIP 中央目录元数据仍由 .NET 读取，调用者应另行限制上传文件大小。加密 ZIP、特殊压缩算法及损坏文件是否可读由 .NET 的 ZIP 支持范围决定，读取失败不会发布结果。

目标目录必须不存在，其父目录必须已存在。解压先写入同一父目录下随机命名的 `.zip-*` 临时目录，全部成功后通过目录移动发布。失败或取消时删除本次临时目录，不修改已有目标，也不创建额外父目录。如果文件系统同时阻止清理，抛出包含原始错误和清理错误的 `AggregateException`，调用者需要处理残留临时目录。Unix 临时目录仅允许当前用户访问；Windows 使用父目录继承的权限，因此应使用受控的父目录。

默认限制为 10,000 个条目、每条目 512 MiB、总计 1 GiB。字节限制按实际解压读取量执行，不依赖 ZIP 声明的原始大小。允许零限制以处理空归档或空文件。目录条目不得携带文件内容。

发布前逐条校验实际读取长度和 ZIP 声明长度一致，并校验 CRC32；空目录也必须声明零长度和零 CRC。CRC32 使用标准 IEEE 多项式的反射形式 `0xEDB88320`，初值为全 1，最终取反。部分 .NET ZIP 读取路径会按声明长度截断可见内容，因此这里只保证可读取内容与声明长度、校验和一致，不能声称验证了归档的全部结构。CRC 能识别常见损坏，不提供签名或来源真实性；攻击者同时修改内容和校验和仍可生成有效归档。

归档文件名使用 `/` 或 `\` 表示层级。拒绝绝对路径、`.`/`..`、空路径段、Windows 设备名、备用数据流、首尾 ASCII 空格、尾部句点及其他 Windows 不可用字符。压缩时拒绝 Unix 文件名中的字面量 `\`，避免解压后改变目录结构。重复路径和文件／目录冲突会失败；路径大小写按操作系统语义比较。只恢复文件内容和目录，不恢复时间戳、权限、可执行位或链接。

压缩不跟随源目录中的符号链接或重解析点，解压拒绝归档中的链接和特殊文件，也检查磁盘目标祖先中的重解析点。文件系统的源目录和目标父目录必须由调用者控制，并在处理期间保持稳定；基于路径的检查无法防御拥有这些目录写权限的进程在检查与打开之间替换路径。

名称兼容规则参考 Microsoft 的[文件命名规则](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)、[ASCII 空格处理说明](https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/file-folder-name-whitespace-characters)及[控制台设备名称](https://learn.microsoft.com/en-us/windows/console/console-handles)。
