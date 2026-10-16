using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.IO;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

public sealed class DemoFileForm
{
    /// <summary>要上传的非空文件，最多 20 MiB；按原始字节保存。</summary>
    [Required]
    public IFormFile? File { get; set; }
}

public sealed class DuplicateFilesRequest
{
    /// <summary>先上传文件得到的当前用户 key，提交 2–64 项。</summary>
    [Required, MinLength(2), MaxLength(64)]
    public string[] Keys { get; set; } = [];
}

public sealed class ZipFilesForm
{
    /// <summary>要打包的文件，1–32 份、实际合计最多 20 MiB；使用不重复的叶子文件名。</summary>
    [Required, MinLength(1), MaxLength(32)]
    public List<IFormFile> Files { get; set; } = [];
}

public sealed record DemoStoredFile(string Key, long Length, DateTimeOffset LastModifiedUtc, string DownloadUrl)
{
    /// <summary>上传时返回的叶子文件名，供调用方保存；存储层不持久化此名称。</summary>
    public string? OriginalFileName { get; init; }
}
public sealed record DemoDuplicateGroup(long Length, string Sha256, IReadOnlyList<string> Keys);
public sealed record DemoExtractedFile(string RelativePath, DemoStoredFile File);
public sealed record DemoExtractionResult(IReadOnlyList<DemoExtractedFile> Files, IReadOnlyList<string> EmptyDirectories);
public sealed record DemoFileHash(string Key, string Sha256);

[LabOnly]
[Route("api/lab/tools/files")]
[Tags("工具示例：文件、摘要与 ZIP")]
public sealed class FileToolsController(ToolDemoFiles files, IWebHostEnvironment environment, IConfiguration configuration)
    : ToolDemoControllerBase(files)
{
    private readonly DemoFileStore _store = new(environment, configuration);

    /// <summary>将上传内容保存为当前用户的私有文件，返回下载地址。</summary>
    /// <remarks>multipart 上传 File，例如 notes.txt。保存使用 Core 的不覆盖写入和随机 key，不复用已有图片上传入口；本接口接受一般文件，不验证其业务格式。保留数据跨进程重启存在，宿主负责磁盘容量和保留期；可用 DELETE 主动清理。</remarks>
    [HttpPost, Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType<DemoStoredFile>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<IActionResult> Upload([FromForm] DemoFileForm form)
    {
        var storage = _store.ForOwner(Owner);
        var bytes = await Files.ReadAsync(form.File, Token);
        var key = StoredFileKey.Create(DemoFileStore.Extension(form.File!.FileName));
        using var input = new MemoryStream(bytes, writable: false);
        await storage.WriteNewAsync(key, input, Token);
        var result = Describe((await storage.GetMetadataAsync(key, Token))!) with
        {
            OriginalFileName = Path.GetFileName(form.File!.FileName.Replace('\\', '/'))
        };
        return Created(result.DownloadUrl, result);
    }

    /// <summary>读取当前用户文件的大小和最后修改时间。</summary>
    /// <remarks>key 使用上传或 ZIP 解压返回的值。其他用户的 key 与不存在的文件均返回 404；结果是查询时元数据，不提供文件系统快照。</remarks>
    [HttpGet("{key}"), Produces("application/json")]
    [ProducesResponseType<DemoStoredFile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Metadata(string key)
    {
        var result = await _store.ForOwner(Owner).GetMetadataAsync(key, Token);
        return result is null ? NotFound() : Ok(Describe(result));
    }

    /// <summary>下载当前用户保存的文件内容。</summary>
    /// <remarks>返回 application/octet-stream 附件，ASP.NET Core 输出并释放文件流。打开后检查 64 MiB 输出上限。只接受 canonical key，不接受目录或服务器路径。</remarks>
    [HttpGet("{key}/content"), Produces("application/octet-stream")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadContent(string key)
    {
        try
        {
            var input = await _store.ForOwner(Owner).OpenReadAsync(key, Token);
            try
            {
                if (input.Length > ToolDemoFiles.MaximumOutputBytes)
                    throw new ToolUploadException(413, "演示结果超过 64 MiB，请缩小输入或选段。");
                return File(input, "application/octet-stream", key);
            }
            catch
            {
                await input.DisposeAsync();
                throw;
            }
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
    }

    /// <summary>删除当前用户保存的文件，缺失时同样成功。</summary>
    /// <remarks>只能删除本人目录下的 canonical key。删除不影响已经打开的读取句柄；取消不会撤销已完成的删除。</remarks>
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string key)
    {
        await _store.ForOwner(Owner).DeleteAsync(key, Token);
        return NoContent();
    }

    /// <summary>计算当前用户文件的 SHA-256 内容摘要。</summary>
    /// <remarks>先上传文件，再传 key。返回 64 位大写十六进制摘要；它不提供文件真实性认证，也不删除内容。</remarks>
    [HttpGet("{key}/sha256"), Produces("application/json")]
    [ProducesResponseType<DemoFileHash>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Hash(string key)
    {
        try
        {
            await using var input = await _store.ForOwner(Owner).OpenReadAsync(key, Token);
            return Ok(new DemoFileHash(key, await FileHashHelper.ComputeSha256Async(input, Token)));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
    }

    /// <summary>在指定的本人文件中发现相同长度和 SHA-256 的重复候选。</summary>
    /// <remarks>先上传至少两份文件，将其 key 放入 keys。只检查本次显式列表，重复 key 会去重；返回至少两个不同文件组成的组，不逐字节确认或自动删除。调用期间应保持文件稳定，缺失文件返回 404。</remarks>
    [HttpPost("duplicates"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(16_384)]
    [ProducesResponseType<IReadOnlyList<DemoDuplicateGroup>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicates(DuplicateFilesRequest request)
    {
        try
        {
            var paths = request.Keys.Select(key => _store.PathFor(Owner, key)).ToArray();
            var groups = await FileHashHelper.FindDuplicatesAsync(paths, Token);
            return Ok(groups.Select(group => new DemoDuplicateGroup(group.Length, group.Sha256,
                group.Paths.Select(Path.GetFileName).Select(name => name!).ToArray())).ToArray());
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
    }

    /// <summary>将上传的普通文件打包为一个可下载 ZIP。</summary>
    /// <remarks>multipart 的 Files 可重复添加。归档使用各文件的叶子名称，不接收服务器目录；名称必须唯一，路径和不安全文件名会失败。最多 32 份且实际合计 20 MiB，原始上传不进入持久文件列表。</remarks>
    [HttpPost("zip/create"), Consumes("multipart/form-data"), Produces("application/zip")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<IActionResult> CreateZip([FromForm] ZipFilesForm form)
    {
        _ = Owner;
        using var workspace = Files.CreateWorkspace();
        var source = workspace.NewPath("contents");
        Directory.CreateDirectory(source);
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long total = 0;
        foreach (var file in form.Files)
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || !names.Add(name))
                throw new ArgumentException("ZIP 中的叶子文件名必须有效且不重复。");
            var bytes = await Files.ReadAsync(file, Token);
            total += bytes.Length;
            if (total > ToolDemoFiles.MaximumFileBytes) throw new ToolUploadException(413, "打包文件实际合计最多 20 MiB。");
            await System.IO.File.WriteAllBytesAsync(Path.Combine(source, name), bytes, Token);
        }
        var output = workspace.NewPath("archive.zip");
        await using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            await ZipArchiveHelper.CreateZipAsync(source, destination, cancellationToken: Token);
        return Download(await workspace.ReadResultAsync(output, Token), "application/zip", "archive.zip");
    }

    /// <summary>安全解压 ZIP，返回本人可下载文件及空目录清单。</summary>
    /// <remarks>上传 File 的扩展名须为 .zip。最多 128 个条目、单文件及实际解压合计最多 20 MiB；路径穿越、链接、CRC 错误和冲突条目会失败。文件用新 key 保存，可按 downloadUrl 下载或 DELETE 清理；relativePath 只作原归档名称展示，不作为磁盘访问参数。</remarks>
    [HttpPost("zip/extract"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType<DemoExtractionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ExtractZip([FromForm] DemoFileForm form)
    {
        var storage = _store.ForOwner(Owner);
        using var workspace = Files.CreateWorkspace();
        var archive = await workspace.SaveAsync(form.File, [".zip"], Token);
        var extracted = workspace.NewPath("extracted");
        await using (var input = System.IO.File.OpenRead(archive))
            await ZipArchiveHelper.ExtractZipAsync(input, extracted,
                new() { MaxEntries = 128, MaxEntryBytes = ToolDemoFiles.MaximumFileBytes, MaxTotalBytes = ToolDemoFiles.MaximumFileBytes }, Token);
        var results = new List<DemoExtractedFile>();
        var created = new List<string>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var key = StoredFileKey.Create(DemoFileStore.Extension(path));
                await using var content = System.IO.File.OpenRead(path);
                await storage.WriteNewAsync(key, content, Token);
                created.Add(key);
                results.Add(new(Path.GetRelativePath(extracted, path).Replace('\\', '/'),
                    Describe((await storage.GetMetadataAsync(key, Token))!)));
            }
            var empty = Directory.EnumerateDirectories(extracted, "*", SearchOption.AllDirectories)
                .Where(path => !Directory.EnumerateFileSystemEntries(path).Any())
                .Select(path => Path.GetRelativePath(extracted, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
            return Ok(new DemoExtractionResult(results.AsReadOnly(), empty));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            foreach (var key in created)
            {
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count > 1) throw new AggregateException("解压文件发布失败且清理未完成。", failures);
            throw;
        }
    }

    private static DemoStoredFile Describe(StoredFileMetadata value) => new(value.Key, value.Length, value.LastModifiedUtc,
        "/api/lab/tools/files/" + value.Key + "/content");
}

internal sealed class DemoFileStore
{
    internal string Root { get; }

    internal DemoFileStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configured = ToolDemoDefaults.Value(configuration, "StorageDirectory") ?? ".tools/tool-demo-files";
        Root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.TrimEndingDirectorySeparator(Root), Path.TrimEndingDirectorySeparator(webRoot), comparison)
            || Root.StartsWith(Path.TrimEndingDirectorySeparator(webRoot) + Path.DirectorySeparatorChar, comparison))
            throw new ToolDependencyException("ToolDemos:StorageDirectory（必须位于 webroot 之外）");
    }

    internal static string OwnerTag(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 256) throw new ArgumentException("用户标识过长。");
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(owner)));
    }

    internal LocalFileStorage ForOwner(string owner) => new(Path.Combine(Root, "files", OwnerTag(owner)));
    internal string PathFor(string owner, string key)
    {
        StoredFileKey.Validate(key);
        return Path.Combine(Root, "files", OwnerTag(owner), key);
    }

    internal static string Extension(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Length is >= 2 and <= 10 && extension.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") < 0
            ? extension : ".bin";
    }
}
