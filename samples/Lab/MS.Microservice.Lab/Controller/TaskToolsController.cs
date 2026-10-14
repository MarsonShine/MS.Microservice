using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.IO;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Tasks;

namespace MS.Microservice.Lab.Controller;

public sealed class IndexDocumentRequest
{
    /// <summary>先调用 POST /api/lab/tools/files 上传文件，使用返回的 key。</summary>
    [Required] public string FileKey { get; set; } = "";
    /// <summary>同一逻辑文档保持相同标识，例如 finance-2026；更新时增加 Version。</summary>
    [Required, StringLength(128)] public string DocumentId { get; set; } = "";
    /// <summary>文档递增版本，首次为 1；删除也须使用更高版本。</summary>
    [Range(typeof(long), "1", "9223372036854775807"), DefaultValue(1L)] public long Version { get; set; } = 1;
    /// <summary>用于文件名搜索的原始名称，例如 财务报告.docx；扩展名须与 FileKey 一致。</summary>
    [Required, StringLength(512)] public string FileName { get; set; } = "";
    /// <summary>用于路径搜索的逻辑路径，例如 /finance/2026/财务报告.docx；省略时使用 FileName，不读取服务器路径。</summary>
    [StringLength(2048)] public string? LogicalPath { get; set; }
    /// <summary>图片须启用；PDF 只对没有文字层的页面执行 OCR。需提前配置 Python 和本地 OCR 模型。</summary>
    [DefaultValue(false)] public bool UseOcr { get; set; }
    /// <summary>可选的稳定任务标识；重放同一标识和参数返回原任务，省略则每次创建新任务。</summary>
    public Guid? JobId { get; set; }
}

public sealed record DemoJob(Guid JobId, string HandlerName, JobState State, int AttemptCount, int MaximumAttempts,
    int Progress, string? ProgressMessage, bool CancellationRequested, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, string? ErrorCode, string? ResultReference);

[LabOnly, Authorize]
[Route("api/lab/tools/tasks")]
[Tags("工具示例：持久任务中心")]
public sealed class TaskToolsController(ToolDemoFiles files, IJobStore jobs,
    IWebHostEnvironment environment, IConfiguration configuration) : ToolDemoControllerBase(files)
{
    private readonly DemoFileStore _store = new(environment, configuration);
    private string Scope => TaskCenterDemo.Scope(Owner);

    /// <summary>后台提取已上传文件的正文，并建立文件名、路径和全文索引。</summary>
    /// <remarks>先上传文件，再提交 key、documentId、fileName；任务默认最多执行 3 次。支持 UTF-8 TXT/MD/JSON/CSV、DOCX、PDF 与启用 OCR 的 PNG/JPEG/WebP。单文件 20 MiB、PDF 50 页、正文 200 万字符。返回 202 后查询任务状态，Succeeded 后即可搜索；使用已有 Coravel 调度器，安全任务在单实例重启后恢复。扫描 PDF/图片需本地 Python、OCR 依赖与模型，接口不自动安装或下载。</remarks>
    [HttpPost("documents/index"), Consumes("application/json"), Produces("application/json")]
    [ProducesResponseType<DemoJob>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IndexDocument(IndexDocumentRequest request)
    {
        StoredFileKey.Validate(request.FileKey);
        var extension = Path.GetExtension(request.FileKey).ToLowerInvariant();
        if (!DocumentIndexJobHandler.Extensions.Contains(extension)) throw new NotSupportedException("不支持此文件扩展名。");
        if (!string.Equals(Path.GetExtension(request.FileName), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("fileName 扩展名须与上传文件 key 一致。");
        if (request.FileName.IndexOfAny(['/', '\\']) >= 0 || string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("fileName 请填写不含目录的文件名。");
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" && !request.UseOcr)
            throw new ArgumentException("图片正文索引须设置 useOcr=true。");
        var owner = Owner;
        var metadata = await _store.ForOwner(owner).GetMetadataAsync(request.FileKey, Token);
        if (metadata is null) return NotFound();
        if (metadata.Length > ToolDemoFiles.MaximumFileBytes) throw new ToolUploadException(413, "单份索引文件最多 20 MiB。");
        var payload = new DocumentIndexPayload(owner, request.FileKey, request.DocumentId, request.Version,
            request.FileName, request.LogicalPath ?? request.FileName, request.UseOcr);
        try
        {
            var job = await jobs.SubmitAsync(JobSubmission.Create(request.JobId ?? Guid.NewGuid(), Scope,
                TaskCenterDemo.IndexHandler, 1, payload, TaskCenterDemo.IndexQueue), Token);
            return Accepted($"/api/lab/tools/tasks/{job.JobId}", Describe(job));
        }
        catch (JobSubmissionConflictException error) { return Conflict(new ProblemDetails { Status = 409, Detail = error.Message }); }
    }

    /// <summary>查看当前用户最近的任务，可按状态筛选。</summary>
    /// <remarks>默认返回最近 50 条，最多 100 条；不返回内部执行令牌或原始任务载荷。只查询当前登录用户。</remarks>
    [HttpGet, Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<DemoJob>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(JobState? state = null, [Range(1, 100)] int take = 50)
        => Ok((await jobs.ListAsync(Scope, state, take, Token)).Select(Describe).ToArray());

    /// <summary>查看任务进度、终态及结果引用。</summary>
    /// <remarks>使用提交返回的 jobId 轮询。Queued 等待执行，Running 正在处理，Succeeded 可搜索索引；Failed/Interrupted/Cancelled 可在次数未耗尽时重试。errorCode 用于定位类别，详细异常查看宿主日志。</remarks>
    [HttpGet("{jobId:guid}"), Produces("application/json")]
    [ProducesResponseType<DemoJob>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId)
    {
        var job = await jobs.GetAsync(Scope, jobId, Token);
        return job is null ? NotFound() : Ok(Describe(job));
    }

    /// <summary>查看一个任务每次执行的进度和结果。</summary>
    /// <remarks>重试保留原 jobId 并新增一次执行记录；历史中不暴露内部执行令牌。其他用户或不存在的任务返回 404。</remarks>
    [HttpGet("{jobId:guid}/attempts"), Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<JobAttemptSnapshot>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Attempts(Guid jobId)
        => await jobs.GetAsync(Scope, jobId, Token) is null ? NotFound()
            : Ok(await jobs.GetAttemptsAsync(Scope, jobId, Token));

    /// <summary>取消等待中的任务，或向运行中的任务发出取消请求。</summary>
    /// <remarks>运行中任务协作响应取消，状态可能暂时仍为 Running。取消不撤销已经发布的索引或外部副作用；先完成的终态不被覆盖。</remarks>
    [HttpPost("{jobId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid jobId) => Update(await jobs.CancelAsync(Scope, jobId, Token));

    /// <summary>重新排队一个失败、中断或已取消的任务。</summary>
    /// <remarks>使用原始载荷和 jobId，执行历史保留；最多执行 3 次，成功任务、运行中任务或次数耗尽返回 409。修正源文件后应上传新文件并以更高文档版本创建新任务。</remarks>
    [HttpPost("{jobId:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid jobId) => Update(await jobs.RetryAsync(Scope, jobId, cancellationToken: Token));

    private IActionResult Update(JobUpdateResult result) => result switch
    {
        JobUpdateResult.Applied => NoContent(), JobUpdateResult.NotFound => NotFound(),
        _ => Conflict(new ProblemDetails { Status = 409, Detail = "当前任务状态或剩余执行次数不允许此操作。" })
    };

    private static DemoJob Describe(JobSnapshot job) => new(job.JobId, job.HandlerName, job.State, job.AttemptCount,
        job.MaximumAttempts, job.Progress, job.ProgressMessage, job.CancellationRequested,
        job.CreatedAtUtc, job.UpdatedAtUtc, job.ErrorCode, job.ResultReference);
}
