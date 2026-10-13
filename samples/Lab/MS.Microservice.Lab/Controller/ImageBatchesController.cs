using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Lab.Infrastructure.Uploads;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Lab.Controller;

public sealed class ImageBatchForm
{
    /// <summary>待处理的 PNG、JPEG 或 WebP 图片，默认每批最多 8 份、合计 20 MiB。</summary>
    public List<IFormFile> Files { get; set; } = [];
    /// <summary>目标宽度，须与 Height 同时填写；不填则保留尺寸。</summary>
    public int? Width { get; set; }
    /// <summary>目标高度，须与 Width 同时填写。</summary>
    public int? Height { get; set; }
    /// <summary>输出格式：0 PNG、1 JPEG、2 WebP，默认 PNG。</summary>
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    /// <summary>缩放模式：0 Contain、1 Cover、2 Stretch，默认等比例适配。</summary>
    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.Contain;
    /// <summary>编码质量 0–100，默认 90；PNG 不使用此值控制文件大小。</summary>
    public int Quality { get; set; } = 90;
}

[LabOnly]
[Authorize]
[ApiController]
[Route("api/lab/tools/image-batches")]
[RequestSizeLimit(SampleUploadOptions.MaximumRequestBodyBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = SampleUploadOptions.MaximumRequestBodyBytes)]
public sealed class ImageBatchesController(ImageBatchJobs jobs, FileUploadValidator validator) : ControllerBase
{
    private string? Owner => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>上传一批图片，后台缩放、转格式并返回任务地址。</summary>
    /// <remarks>宿主须启用 ImageBatches:Enabled。创建后轮询 GET 状态，整个批次成功后通过 files/{index} 下载；任务元数据重启后丢失，文件保留直到删除。</remarks>
    [HttpPost, Consumes("multipart/form-data")]
    [ProducesResponseType<ImageBatchSnapshot>(202)]
    public async Task<IActionResult> Submit([FromForm] ImageBatchForm form)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        if (form.Files is null || form.Files.Count < 1 || form.Files.Count > jobs.MaximumFiles
            || !Enum.IsDefined(form.Format) || !Enum.IsDefined(form.ResizeMode) || form.Quality is < 0 or > 100
            || form.Width.HasValue != form.Height.HasValue || form.Width is < 1 or > 4096 || form.Height is < 1 or > 4096
            || (long)(form.Width ?? 1) * (form.Height ?? 1) > 4_000_000)
            return BadRequest("文件数量、尺寸或输出格式无效。");
        var extensions = new List<string>();
        foreach (var file in form.Files)
        {
            var valid = await validator.ValidateAsync(file, UploadFileKind.Image, jobs.MaximumFileBytes, HttpContext.RequestAborted);
            if (!valid.IsValid) return StatusCode(valid.StatusCode, valid.Message);
            extensions.Add(valid.Extension);
        }
        var inputs = new List<Stream>();
        Exception? failure = null;
        try
        {
            foreach (var file in form.Files) inputs.Add(file.OpenReadStream());
            var result = await jobs.SubmitAsync(Owner, inputs.Select((stream, index) => new ImageBatchInput(stream, extensions[index])).ToArray(),
                new ImageProcessingOptions
                {
                    Format = form.Format, Quality = form.Quality, ResizeMode = form.ResizeMode,
                    Resize = form.Width.HasValue ? new(form.Width.Value, form.Height!.Value) : null,
                    Limits = new() { MaximumDimension = 4096, MaximumPixels = 4_000_000, MaximumEncodedBytes = jobs.MaximumFileBytes }
                }, HttpContext.RequestAborted);
            return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (ImageBatchCapacityException error) { failure = error; return StatusCode(StatusCodes.Status429TooManyRequests, "任务或保留记录已满，请先删除已结束任务。"); }
        catch (InvalidDataException error) { failure = error; return StatusCode(StatusCodes.Status413PayloadTooLarge, "批次实际文件大小超过限制。"); }
        catch (Exception error) { failure = error; throw; }
        finally { await ImageBatchJobs.DisposeInputsAsync(inputs, failure); }
    }

    /// <summary>查询本人的图片批次状态、进度和结果列表。</summary>
    /// <remarks>state 为 0 准备、1 排队、2 处理中、3 成功、4 失败、5 取消。只有整个批次成功才可下载，其他用户的任务不可见。</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ImageBatchSnapshot>(200)]
    public IActionResult Get(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        return jobs.Get(Owner, id) is { } result ? Ok(result) : NotFound();
    }

    /// <summary>取消本人的图片批处理任务。</summary>
    /// <remarks>排队任务立即取消；正在执行的步骤可能需要等待，继续查询 GET 状态确认结束。</remarks>
    [HttpPost("{id:guid}/cancel")]
    public IActionResult Cancel(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        return jobs.Cancel(Owner, id) is { } result ? Ok(result) : NotFound();
    }

    /// <summary>从失败或取消批次保留的输入创建新任务。</summary>
    /// <remarks>返回新的任务 Id 和状态地址，不自动重试原任务；只有输入仍保留时可重试。</remarks>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType<ImageBatchSnapshot>(202)]
    public async Task<IActionResult> Retry(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        try
        {
            var result = await jobs.RetryAsync(Owner, id, HttpContext.RequestAborted);
            return result is null ? NotFound() : AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (ImageBatchCapacityException) { return StatusCode(StatusCodes.Status429TooManyRequests); }
        catch (InvalidOperationException) { return Conflict("只能重试失败或取消且仍保留输入的任务。"); }
    }

    /// <summary>下载本人成功批次中的一份图片。</summary>
    /// <remarks>index 从 0 开始，对应状态返回的结果顺序；只有整个批次成功才可下载。</remarks>
    [HttpGet("{id:guid}/files/{index:int}")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType<FileStreamResult>(200)]
    public async Task<IActionResult> Download(Guid id, int index)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        var result = await jobs.OpenResultAsync(Owner, id, index, HttpContext.RequestAborted);
        return result is null ? NotFound() : File(result.Content, result.ContentType);
    }

    /// <summary>删除本人已结束的图片批次和保留文件。</summary>
    /// <remarks>进行中或正在复制以供重试的任务须先等待结束。删除后不能再下载或重试。</remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public IActionResult Delete(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        try { return jobs.Delete(Owner, id) ? NoContent() : NotFound(); }
        catch (InvalidOperationException) { return Conflict("请等待处理或重试复制结束后删除任务。"); }
    }
}
