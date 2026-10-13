using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Core.IO;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Media.Inference.Tables;
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;

namespace MS.Microservice.Lab.Controller;

public sealed class InferenceImageForm
{
    /// <summary>待处理静态 PNG、JPEG 或 WebP 图片，最多 20 MiB、4096 单边、4M 像素。</summary>
    [Required]
    public IFormFile? File { get; set; }
}

public sealed class ImageRepairForm
{
    /// <summary>待修复的静态图片。</summary>
    [Required]
    public IFormFile? File { get; set; }
    /// <summary>与方向校正后原图等大的不透明二值蒙版，白色修复、黑色保留。</summary>
    [Required]
    public IFormFile? Mask { get; set; }
}

public sealed class VideoRepairForm
{
    /// <summary>待处理 MP4、MOV、MKV 或 WebM；最多 20 MiB、120 帧、20 秒，宽×高×实际帧数合计不超过 4000 万。</summary>
    [Required]
    public IFormFile? File { get; set; }
    /// <summary>省略时自动识别水印；提供时使用与视频显示画面等大的静态二值 PNG 蒙版，白色修复。</summary>
    public IFormFile? Mask { get; set; }
}

/// <summary>上传图片或短视频，查看本地模型的识别和处理效果。</summary>
[LabOnly]
[Route("api/lab/tools/inference")]
public sealed class InferenceToolsController(ToolDemoFiles files, ToolInferenceRuntime runtime, IConfiguration configuration)
    : ToolDemoControllerBase(files)
{
    /// <summary>识别图片中的中英文印刷文字，返回文字、位置和识别分数。</summary>
    /// <remarks>需配置 ToolDemos:PythonExecutablePath、OcrModelDirectory，并安装 ocr-requirements.txt 中的依赖。返回 JSON；适合正常方向横排印刷体，不保证手写、多栏阅读顺序或低对比度文字识别。</remarks>
    [HttpPost("ocr")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<OcrResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> Ocr([FromForm] InferenceImageForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        return Ok(await runtime.RunAsync(async token =>
        {
            using var input = new MemoryStream(bytes);
            return await new OcrProcessor(runtime.OcrOptions()).RecognizeAsync(input, token);
        }, Token));
    }

    /// <summary>识别带完整线框的表格，返回单元格、合并范围和文字。</summary>
    /// <remarks>复用 OCR 的 Python 和模型配置，无需额外表格模型。仅支持轴对齐、封闭线框，默认格间距至少 40 像素；返回 JSON，行列从 0 开始。未分配的文字仍在 Ocr 和 UnassignedRegionIndices 中，结果需要复核。</remarks>
    [HttpPost("tables")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<TableRecognitionResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> Tables([FromForm] InferenceImageForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        return Ok(await runtime.RunAsync(async token =>
        {
            using var input = new MemoryStream(bytes);
            return await new TableRecognizer(new() { Ocr = runtime.OcrOptions() }).RecognizeAsync(input, token);
        }, Token));
    }

    /// <summary>自动检测图片水印并修复，下载结果、检测蒙版和报告 ZIP。</summary>
    /// <remarks>需配置 PythonExecutablePath、MiganModelPath、WatermarkDetectorModelPath 并安装检测依赖。自动检测主要针对重复文字，可能误选字幕、Logo 或普通文字；请检查 mask.png 和 result.png，必要时用手动修复接口纠正。所有配置均位于 ToolDemos。</remarks>
    [HttpPost("watermarks/remove")]
    [Consumes("multipart/form-data")]
    [Produces("application/zip")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> RemoveWatermark([FromForm] InferenceImageForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        var zip = await runtime.RunAsync(async token =>
        {
            using var workspace = Files.CreateWorkspace();
            using var input = new MemoryStream(bytes);
            using var image = new MemoryStream();
            using var mask = new MemoryStream();
            var processor = await runtime.AutomaticAsync(token);
            var result = await processor.RemoveAsync(input, image, mask, cancellationToken: token);
            await System.IO.File.WriteAllBytesAsync(workspace.NewPath("result.png"), image.ToArray(), token);
            await System.IO.File.WriteAllBytesAsync(workspace.NewPath("mask.png"), mask.ToArray(), token);
            await System.IO.File.WriteAllTextAsync(workspace.NewPath("report.json"), JsonSerializer.Serialize(result), token);
            using var output = new MemoryStream();
            await ZipArchiveHelper.CreateZipAsync(workspace.DirectoryPath, output, cancellationToken: token);
            return output.ToArray();
        }, Token);
        return Download(zip, "application/zip", "watermark-removal.zip");
    }

    /// <summary>按上传蒙版修复图片选定区域，下载 PNG。</summary>
    /// <remarks>只需 ToolDemos:MiganModelPath，无需 Python。蒙版白色区域会重新生成，黑色区域保留；严格要求不透明、仅黑白且尺寸一致。不能恢复被遮挡的真实内容，原图不会修改。</remarks>
    [HttpPost("repair")]
    [Consumes("multipart/form-data")]
    [Produces("image/png")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> Repair([FromForm] ImageRepairForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        var maskBytes = await Files.ReadAsync(form.Mask, Token, UploadFileKind.Image);
        var result = await runtime.RunAsync(async token =>
        {
            using var input = new MemoryStream(bytes);
            using var mask = new MemoryStream(maskBytes);
            using var output = new MemoryStream();
            var processor = await runtime.RepairAsync(token);
            await processor.InpaintAsync(input, mask, output, token);
            return output.ToArray();
        }, Token);
        return Download(result, "image/png", "repaired.png");
    }

    /// <summary>移除图片背景，下载保留透明度的 PNG。</summary>
    /// <remarks>需配置 ToolDemos:BackgroundRemovalModelPath，使用 .NET CPU 推理，无需 Python。首轮加载较慢，本机已观测单任务峰值约 6.4 GiB，宿主须留出余量；复杂发丝、边缘或背景可能影响效果，结果需要复核。Lab 媒体推理请求统一串行执行。</remarks>
    [HttpPost("background/remove")]
    [Consumes("multipart/form-data")]
    [Produces("image/png")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> RemoveBackground([FromForm] InferenceImageForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        var result = await runtime.RunAsync(async token =>
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            var processor = await runtime.BackgroundAsync(token);
            await processor.RemoveAsync(input, output, cancellationToken: token);
            return output.ToArray();
        }, Token);
        return Download(result, "image/png", "foreground.png");
    }

    /// <summary>用 Swin2SR 将图片宽高各放大两倍，下载 PNG。</summary>
    /// <remarks>需配置 ToolDemos:SuperResolutionModelPath，无需 Python。640×480 会输出 1280×960；模型可能生成伪细节、改变文字或产生分块接缝，透明度独立放大，请检查实际效果。</remarks>
    [HttpPost("upscale")]
    [Consumes("multipart/form-data")]
    [Produces("image/png")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> Upscale([FromForm] InferenceImageForm form)
    {
        var bytes = await Files.ReadAsync(form.File, Token, UploadFileKind.Image);
        var result = await runtime.RunAsync(async token =>
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            var processor = await runtime.UpscalerAsync(token);
            await processor.UpscaleAsync(input, output, token);
            return output.ToArray();
        }, Token);
        return Download(result, "image/png", "upscaled.png");
    }

    /// <summary>自动去除短视频水印，或按静态蒙版修复，下载效果预览 ZIP。</summary>
    /// <remarks>省略 Mask 使用自动检测，提供 Mask 使用手动模式。需 Python、video-requirements.txt、FFmpeg、MiganModelPath；自动模式还需 WatermarkDetectorModelPath 及检测依赖。最多 120 帧、20 秒、每帧 1M 像素，宽×高×实际帧数合计不超过 4000 万。ZIP 包含 FFV1 MKV、蒙版预览和首帧图片，保留音频；效果须复核。需要 MP4 时可再使用视频编辑接口转码。</remarks>
    [HttpPost("videos/repair")]
    [Consumes("multipart/form-data")]
    [Produces("application/zip")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> RepairVideo([FromForm] VideoRepairForm form)
    {
        var zip = await runtime.RunAsync(async token =>
        {
            var options = new VideoWatermarkRemovalOptions
            {
                PythonExecutablePath = runtime.RequiredFile("PythonExecutablePath"), MiganModelPath = runtime.RequiredFile("MiganModelPath"),
                FfmpegExecutablePath = configuration["ToolDemos:FfmpegExecutablePath"] ?? "ffmpeg", CpuThreads = Math.Min(2, Environment.ProcessorCount),
                Limits = new() { MaximumInputBytes = ToolDemoFiles.MaximumFileBytes, MaximumOutputBytes = ToolDemoFiles.MaximumOutputBytes,
                    MaximumFrames = 120, MaximumDurationSeconds = 20, MaximumFramePixels = 1_000_000, MaximumTotalPixels = 40_000_000 },
                Detector = form.Mask is null ? new() { ModelPath = runtime.RequiredFile("WatermarkDetectorModelPath") } : null
            };
            using var workspace = Files.CreateWorkspace();
            var input = await workspace.SaveAsync(form.File, [".mp4", ".mkv", ".webm", ".mov"], token);
            var outputDirectory = workspace.NewPath("result");
            await using var processor = new VideoWatermarkRemover(options);
            if (form.Mask is null) await processor.RemoveAsync(input, outputDirectory, token);
            else
            {
                var mask = await workspace.SaveAsync(form.Mask, [".png"], token);
                await processor.RemoveAsync(input, outputDirectory, new() { StaticMaskPath = mask }, token);
            }
            using var output = new MemoryStream();
            await ZipArchiveHelper.CreateZipAsync(outputDirectory, output, cancellationToken: token);
            return output.ToArray();
        }, Token);
        return Download(zip, "application/zip", "video-repair.zip");
    }
}
