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
    public List<IFormFile> Files { get; set; } = [];
    public int? Width { get; set; }
    public int? Height { get; set; }
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.Contain;
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

    [HttpPost]
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

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        return jobs.Get(Owner, id) is { } result ? Ok(result) : NotFound();
    }

    [HttpPost("{id:guid}/cancel")]
    public IActionResult Cancel(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        return jobs.Cancel(Owner, id) is { } result ? Ok(result) : NotFound();
    }

    [HttpPost("{id:guid}/retry")]
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

    [HttpGet("{id:guid}/files/{index:int}")]
    public async Task<IActionResult> Download(Guid id, int index)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        var result = await jobs.OpenResultAsync(Owner, id, index, HttpContext.RequestAborted);
        return result is null ? NotFound() : File(result.Content, result.ContentType);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (!jobs.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(Owner)) return Unauthorized();
        try { return jobs.Delete(Owner, id) ? NoContent() : NotFound(); }
        catch (InvalidOperationException) { return Conflict("请等待处理或重试复制结束后删除任务。"); }
    }
}
