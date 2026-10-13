using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Lab.Application.Tools;

[ApiController]
[ServiceFilter(typeof(ToolDemoExceptionFilter))]
[RequestSizeLimit(ToolDemoFiles.MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ToolDemoFiles.MaximumRequestBytes)]
[ProducesResponseType<ProblemDetails>(400, "application/problem+json")]
[ProducesResponseType(401)]
[ProducesResponseType<ProblemDetails>(413, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(415, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(422, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(504, "application/problem+json")]
public abstract class ToolDemoControllerBase(ToolDemoFiles files) : ControllerBase
{
    protected ToolDemoFiles Files => files;
    protected CancellationToken Token => HttpContext.RequestAborted;
    protected string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ToolIdentityException();
    protected FileContentResult Download(byte[] content, string mime, string filename)
    {
        if (content.Length > ToolDemoFiles.MaximumOutputBytes)
            throw new ToolUploadException(StatusCodes.Status413PayloadTooLarge, "演示结果超过 64 MiB，请缩小输入或选段。");
        return File(content, mime, filename);
    }
}

public sealed class ToolDependencyException(string configurationKey)
    : Exception($"请在宿主配置中准备 {configurationKey} 所需的环境、文件或凭据后重试。");

internal sealed class ToolIdentityException : Exception;

public sealed class ToolUploadException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ToolDemoExceptionFilter(ILogger<ToolDemoExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested) return;
        var error = context.Exception;
        var response = error switch
        {
            ToolIdentityException => (401, "需要包含用户标识的 Lab 登录身份。"),
            ToolUploadException upload => (upload.StatusCode, upload.Message),
            ToolDependencyException dependency => (503, dependency.Message),
            Win32Exception or FileNotFoundException or DirectoryNotFoundException or PlatformNotSupportedException
                => (503, "所需的本地运行环境或文件尚未准备，请检查该接口的 Swagger 前提说明和 ToolDemos 配置。"),
            TimeoutException => (504, "工具处理超时，请缩小输入或选段后重试。"),
            MediaProcessException => (422, "媒体处理未完成，请检查文件格式、处理参数和服务器工具环境。"),
            ArgumentException or FormatException or JsonException => (400, error.Message),
            InvalidDataException or NotSupportedException => (422, error.Message),
            _ => (0, string.Empty)
        };
        if (response.Item1 == 0) return;
        logger.LogWarning(error, "工具演示返回 {StatusCode}：{Route}", response.Item1, context.HttpContext.Request.Path);
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = response.Item1,
            Title = "工具演示请求未完成",
            Detail = response.Item2
        }) { StatusCode = response.Item1 };
        context.ExceptionHandled = true;
    }
}

public sealed class ToolDemoFiles(FileUploadValidator validator, ILogger<ToolDemoFiles> logger)
{
    public const long MaximumRequestBytes = 25 * 1024 * 1024;
    public const int MaximumFileBytes = 20 * 1024 * 1024;
    public const int MaximumOutputBytes = 64 * 1024 * 1024;

    public async Task<byte[]> ReadAsync(IFormFile? file, CancellationToken token, UploadFileKind? kind = null)
    {
        if (file is null || file.Length == 0) throw new ArgumentException("请选择非空文件。");
        if (file.Length > MaximumFileBytes) throw new ToolUploadException(413, "单份演示文件最多 20 MiB。");
        if (kind.HasValue)
        {
            var result = await validator.ValidateAsync(file, kind.Value, MaximumFileBytes, token);
            if (!result.IsValid) throw new ToolUploadException(result.StatusCode, result.Message);
        }
        await using var input = file.OpenReadStream();
        return await ReadBoundedAsync(input, MaximumFileBytes, token);
    }

    public ToolDemoWorkspace CreateWorkspace() => new(this, logger);
    public Task<byte[]> ReadOutputAsync(Stream input, CancellationToken token) => ReadBoundedAsync(input, MaximumOutputBytes, token);

    internal static async Task<byte[]> ReadBoundedAsync(Stream input, int maximumBytes, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = (int)Math.Min(buffer.Length, (long)maximumBytes - output.Length + 1);
            var read = await input.ReadAsync(buffer.AsMemory(0, count), token);
            if (read == 0) return output.ToArray();
            if (read > maximumBytes - output.Length)
                throw new ToolUploadException(413, "文件实际大小超过演示上限。");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }
}

public sealed class ToolDemoWorkspace : IDisposable
{
    private readonly ToolDemoFiles _files;
    private readonly ILogger _logger;
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ms-lab-tool-" + Guid.NewGuid().ToString("N"));
    internal ToolDemoWorkspace(ToolDemoFiles files, ILogger logger)
    {
        _files = files;
        _logger = logger;
        Directory.CreateDirectory(DirectoryPath);
    }

    public string NewPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName is "." or "..")
            throw new ArgumentException("演示工作文件必须使用单层文件名。");
        var path = Path.Combine(DirectoryPath, fileName);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("演示目标已存在。");
        return path;
    }

    public async Task<string> SaveAsync(IFormFile? file, string[] allowedExtensions, CancellationToken token)
    {
        var extension = Path.GetExtension(file?.FileName ?? string.Empty).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ToolUploadException(415, "支持的文件扩展名：" + string.Join("、", allowedExtensions));
        var bytes = await _files.ReadAsync(file, token);
        var path = NewPath(Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, bytes, token);
        return path;
    }

    public async Task<byte[]> ReadResultAsync(string path, CancellationToken token)
    {
        if (!Path.GetFullPath(path).StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("只能读取当前演示工作目录中的结果。");
        await using var input = File.OpenRead(path);
        return await ToolDemoFiles.ReadBoundedAsync(input, ToolDemoFiles.MaximumOutputBytes, token);
    }

    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Cleanup must not replace a processing exception or invalidate an already completed download.
            _logger.LogWarning(error, "工具演示暂存目录未能清理：{Directory}", DirectoryPath);
        }
    }
}
