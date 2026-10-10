# ZIP 归档设计

使用示例和限额见 [Core README](../README.md#zip)。实现仅依赖 .NET `ZipArchive`，范围是普通文件和目录。

创建使用相对条目名，保留空目录，不额外包一层根目录。源符号链接／重解析点会失败；直接传入位于源树内的输出 `FileStream` 会被拒绝。包装流或硬链接别名无法仅按路径识别，输出隔离仍是调用者责任。

解压要求可定位输入，避免 `ZipArchive` 为不可定位流做完整内存缓冲。内容使用 80 KiB 缓冲，中央目录元数据仍由 .NET 加载，因此条目数量限制不能替代上传归档大小限制。

每个条目先规范化分隔符并检查相对路径，再登记显式条目和隐式父目录。该索引拒绝重复路径、文件作为父目录及文件／目录冲突；比较策略遵循操作系统大小写语义。禁止点路径、绝对路径、空段、设备名、备用数据流、首尾 ASCII 空格、尾点和 Windows 不可用字符。源文件名的字面量反斜杠也会拒绝，避免 Unix 文件在解压时变成目录层级。

归档外部属性中的符号链接、重解析点和特殊文件会失败；磁盘目标及祖先也检查重解析点。路径检查无法关闭恶意本机进程在检查后替换路径的竞态，源树与目标父目录必须可信、稳定。

实际读取量决定单条和总字节预算。完成条目后核对声明长度和 CRC32；目录必须长度和 CRC 均为零，且实际没有内容。CRC 使用反射 IEEE 多项式 `0xEDB88320`，初值全 1，最终取反。部分 .NET 读取路径按声明长度截断可见内容，因此这里只验证可读内容与元数据一致，不宣称完整结构验证或密码学真实性。

所有内容写入目标父目录内随机 `.zip-*` 目录，成功后移动为目标；既有目标不覆盖。失败删除本次目录，清理同时失败则聚合原错误。Unix 暂存目录为当前用户访问，Windows 继承父目录权限。时间戳、执行位、权限与链接不恢复。

测试见 [ZipArchiveHelperTests](../../../test/MS.Microservice.Core.Tests/IO/ZipArchiveHelperTests.cs)：跨平台路径、目录冲突、伪造长度／CRC、已知 CRC 向量、实际字节限额、取消与竞争目标所有权。命名依据见 Microsoft [文件命名规则](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)及[ASCII 空格规则](https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/file-folder-name-whitespace-characters)。
