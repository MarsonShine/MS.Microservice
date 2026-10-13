using System.ComponentModel.DataAnnotations;
using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.IO;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Storage.S3;

namespace MS.Microservice.Lab.Controller;

public sealed class S3ToolSettings
{
    public bool Enabled { get; set; }
    public string BucketName { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }
    public string Prefix { get; set; } = "lab-tools";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string? SessionToken { get; set; }
}

public sealed record S3UploadResult(string Key, int Bytes, string MetadataUrl, string DownloadUrl);
public sealed record S3ReadUrlResult(string Key, string Url, int RequestedLifetimeSeconds);

[LabOnly]
[Route("api/lab/tools/s3")]
[Tags("工具示例：S3 对象存储")]
public sealed class S3ToolsController(ToolDemoFiles files, IConfiguration configuration) : ToolDemoControllerBase(files)
{
    /// <summary>在本人 S3 命名空间下条件新建一个对象，不覆盖已有内容。</summary>
    /// <param name="key">预先持有的 canonical key，例如 9f85cd771b75429696da3988d0686355.bin；也可使用本地上传返回的 key。</param>
    /// <param name="form">multipart 上传的非空 File，最多 20 MiB。</param>
    /// <remarks>multipart 上传 File，并保留 key 以便失败后核对。宿主须配置 ToolDemos:S3 的 Enabled、BucketName、Region、AccessKeyId、SecretAccessKey，可选 ServiceUrl、ForcePathStyle、SessionToken、Prefix；桶须预先存在，兼容端点须支持条件新建。PUT、取消或暂存清理异常时远端可能已提交，应查询该 key，不自动回滚或重试。</remarks>
    [HttpPut("{key}"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType<S3UploadResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Upload(string key, [FromForm] DemoFileForm form)
    {
        StoredFileKey.Validate(key);
        using var workspace = Files.CreateWorkspace();
        using var scope = Connect(workspace.DirectoryPath);
        var bytes = await Files.ReadAsync(form.File, Token);
        using var content = new MemoryStream(bytes, writable: false);
        try { await scope.Storage.WriteNewAsync(key, content, Token); }
        catch (IOException error) when (error.InnerException is AmazonS3Exception { StatusCode: HttpStatusCode.PreconditionFailed })
        {
            return Conflict(new ProblemDetails { Status = 409, Title = "该 key 已存在，未覆盖对象。" });
        }
        var result = new S3UploadResult(key, bytes.Length, "/api/lab/tools/s3/" + key, "/api/lab/tools/s3/" + key + "/content");
        return Created(result.MetadataUrl, result);
    }

    /// <summary>读取本人 S3 对象的大小和最后修改时间。</summary>
    /// <remarks>只查询固定宿主桶和当前用户 prefix。对象缺失返回 404；HEAD 通用 404 无法始终区分错误桶，宿主应先确认桶、凭据及权限。</remarks>
    [HttpGet("{key}"), Produces("application/json")]
    [ProducesResponseType<StoredFileMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Metadata(string key)
    {
        StoredFileKey.Validate(key);
        using var workspace = Files.CreateWorkspace();
        using var scope = Connect(workspace.DirectoryPath);
        var value = await scope.Storage.GetMetadataAsync(key, Token);
        return value is null ? NotFound() : Ok(value);
    }

    /// <summary>下载本人 S3 对象的实际字节。</summary>
    /// <remarks>完整读取且通过 64 MiB 输出预算后才返回附件；下载流和整个 HTTP 响应由组件适配器释放。对象缺失返回 404，不将上传预算当成读取上限。</remarks>
    [HttpGet("{key}/content"), Produces("application/octet-stream")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> DownloadObject(string key)
    {
        StoredFileKey.Validate(key);
        using var workspace = Files.CreateWorkspace();
        using var scope = Connect(workspace.DirectoryPath);
        try
        {
            await using var content = await scope.Storage.OpenReadAsync(key, Token);
            return Download(await Files.ReadOutputAsync(content, Token), "application/octet-stream", key);
        }
        catch (FileNotFoundException) { return NotFound(); }
    }

    /// <summary>删除本人 S3 命名空间下的当前对象，缺失时同样成功。</summary>
    /// <remarks>该操作不会清理版本桶历史或改动 ACL。网络或取消异常时应核对远端当前状态，组件不自动重试。</remarks>
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(string key)
    {
        StoredFileKey.Validate(key);
        using var workspace = Files.CreateWorkspace();
        using var scope = Connect(workspace.DirectoryPath);
        await scope.Storage.DeleteAsync(key, Token);
        return NoContent();
    }

    /// <summary>为本人对象创建限时 GET URL，供授权下载使用。</summary>
    /// <param name="key">本人对象的 canonical key。</param>
    /// <param name="seconds">请求有效期，默认 600 秒，范围 1–604800 秒。</param>
    /// <remarks>URL 是 bearer 凭据，只应发给获授权的人；临时凭据到期可能缩短有效期。签名不验证对象存在，不保证相同 URL 可用于其他 S3 兼容服务。SDK 签名仅在调用前后检查取消。</remarks>
    [HttpGet("{key}/read-url"), Produces("application/json")]
    [ProducesResponseType<S3ReadUrlResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> CreateReadUrl(string key, [FromQuery, Range(1, 604_800)] int seconds = 600)
    {
        StoredFileKey.Validate(key);
        using var workspace = Files.CreateWorkspace();
        using var scope = Connect(workspace.DirectoryPath);
        var url = await scope.Storage.CreateReadUrlAsync(key, TimeSpan.FromSeconds(seconds), Token);
        Response.Headers.CacheControl = "no-store";
        return Ok(new S3ReadUrlResult(key, url, seconds));
    }

    private S3Connection Connect(string stagingDirectory)
    {
        const string key = "ToolDemos:S3";
        AmazonS3Client? client = null;
        var owner = DemoFileStore.OwnerTag(Owner);
        try
        {
            var options = configuration.GetSection(key).Get<S3ToolSettings>();
            if (options is null || !options.Enabled || string.IsNullOrWhiteSpace(options.AccessKeyId)
                || string.IsNullOrWhiteSpace(options.SecretAccessKey) || string.IsNullOrWhiteSpace(options.Region))
                throw new ArgumentException();
            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(30)
            };
            if (options.ServiceUrl is not null)
            {
                if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new ArgumentException();
                config.ServiceURL = uri.AbsoluteUri;
            }
            AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
                ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
                : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);
            client = new AmazonS3Client(credentials, config);
            var prefix = options.Prefix.Length == 0 ? "owners/" + owner : options.Prefix + "/owners/" + owner;
            var storage = new S3FileStorage(client, new()
            {
                BucketName = options.BucketName, Prefix = prefix, TemporaryDirectory = stagingDirectory,
                MaximumUploadBytes = ToolDemoFiles.MaximumFileBytes
            });
            return new(client, storage);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            client?.Dispose();
            throw new ToolDependencyException(key);
        }
    }

    private sealed record S3Connection(AmazonS3Client Client, S3FileStorage Storage) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
