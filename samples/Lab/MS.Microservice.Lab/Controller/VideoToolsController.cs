using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Lab.Controller;

[LabOnly, Authorize, ApiController]
[Route("api/lab/tools/videos")]
[RequestSizeLimit(ToolDemoFiles.MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ToolDemoFiles.MaximumRequestBytes)]
public sealed class VideoToolsController(ToolDemoFiles files, IConfiguration configuration) : ToolDemoControllerBase(files)
{
    private static readonly string[] InputExtensions = [".mp4", ".mkv", ".webm"];
    private readonly VideoProcessor _processor = new(new()
    {
        FfmpegPath = ToolDemoDefaults.Value(configuration, "FfmpegExecutablePath") ?? "ffmpeg",
        FfprobePath = ToolDemoDefaults.Value(configuration, "FfprobeExecutablePath") ?? "ffprobe",
        Timeout = TimeSpan.FromMinutes(2)
    });

    /// <summary>读取上传视频的画面、时长、帧率和音轨信息。</summary>
    /// <remarks>需宿主安装ffprobe，默认使用PATH命令，可配置ToolDemos:FfprobeExecutablePath。上传MP4/MKV/WebM，最多20MiB；单次原生命令最多2分钟。</remarks>
    [HttpPost("probe"), Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(VideoInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> Probe([FromForm] VideoUploadForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        return Ok(await _processor.ProbeAsync(input, Token));
    }

    /// <summary>裁剪时间、变换画面并转换视频格式。</summary>
    /// <remarks>需已安装ffmpeg与ffprobe，可由ToolDemos配置可执行文件路径。先裁剪画面，再旋转/翻转、缩放；输出奇数边长补齐为偶数，保留音轨并重新编码。输入输出限4096边长、400万像素，结果最多64MiB，单次原生命令最多2分钟。</remarks>
    [HttpPost("process"), Consumes("multipart/form-data")]
    [Produces("video/mp4", "video/x-matroska", "video/webm")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Process([FromForm] VideoTransformForm form)
    {
        var outputType = VideoOutput(form.Format);
        var options = new VideoEditOptions
        {
            Start = MediaToolParameters.Time(form.StartSeconds),
            Duration = form.DurationSeconds is { } duration ? MediaToolParameters.Time(duration) : null,
            Crop = MediaToolParameters.Crop(form.CropX, form.CropY, form.CropWidth, form.CropHeight),
            Resize = MediaToolParameters.Size(form.Width, form.Height), ResizeMode = form.ResizeMode,
            RotationDegrees = form.RotationDegrees, FlipHorizontal = form.FlipHorizontal, FlipVertical = form.FlipVertical,
            ConstantRateFactor = form.ConstantRateFactor, Speed = form.Speed,
            MaximumDimension = 4096, MaximumPixels = 4_000_000
        };
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("video" + outputType.Extension);
        await _processor.ProcessAsync(input, output, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "video" + outputType.Extension);
    }

    /// <summary>按上传顺序拼接不同尺寸、帧率的视频。</summary>
    /// <remarks>上传1至4段短视频，整个请求最多25MiB。统一画布与帧率，等比包含并补黑边；有音频时统一48kHz立体声并为无音轨片段补静音。每段最多延长一帧以对齐时间线。需ffmpeg/ffprobe，每次原生命令最多2分钟，结果最多64MiB。</remarks>
    [HttpPost("concatenate"), Consumes("multipart/form-data")]
    [Produces("video/mp4", "video/x-matroska", "video/webm")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Concatenate([FromForm] VideoConcatenateForm form)
    {
        if (form.Files is null || form.Files.Count is < 1 or > 4)
            throw new ArgumentException("请按顺序上传1至4段视频。");
        var outputType = VideoOutput(form.Format);
        var options = new VideoConcatenationOptions
        {
            Canvas = MediaToolParameters.Size(form.Width, form.Height), FrameRate = form.FrameRate,
            MaximumInputs = 4, ConstantRateFactor = form.ConstantRateFactor, Speed = form.Speed,
            MaximumDimension = 4096, MaximumPixels = 4_000_000
        };
        using var workspace = Files.CreateWorkspace();
        var inputs = new List<string>();
        foreach (var file in form.Files) inputs.Add(await workspace.SaveAsync(file, InputExtensions, Token));
        var output = workspace.NewPath("joined" + outputType.Extension);
        await _processor.ConcatenateAsync(inputs, output, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "joined" + outputType.Extension);
    }

    /// <summary>截取视频指定时间点的图片或封面。</summary>
    /// <remarks>截取时间点处或之后的首个可用帧，可选择PNG/JPEG/WebP并等比缩小。默认截取0秒；必须早于视频结尾。需ffmpeg/ffprobe，输入画面及截图最多400万像素，每次原生命令最多2分钟。</remarks>
    [HttpPost("frame"), Consumes("multipart/form-data")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Frame([FromForm] VideoFrameForm form)
    {
        var outputType = MediaToolParameters.ImageOutput(form.Format);
        var timestamp = MediaToolParameters.Time(form.TimestampSeconds);
        var options = new ImageProcessingOptions
        {
            Format = form.Format, Quality = form.Quality, Resize = MediaToolParameters.Size(form.Width, form.Height),
            Limits = MediaToolParameters.Limits
        };
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("frame" + outputType.Extension);
        await _processor.CaptureFrameAsync(input, output, timestamp, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "frame" + outputType.Extension);
    }

    /// <summary>提取视频中的指定音轨。</summary>
    /// <remarks>支持WAV（PCM16）、MP3、M4A、FLAC，可裁剪时间段。保留所选音轨的采样率及声道；MP3仅支持单声道/立体声，编码器不支持原采样率时返回错误。需ffmpeg/ffprobe，无音轨视频不能提取。结果最多64MiB，每次原生命令最多2分钟。</remarks>
    [HttpPost("audio"), Consumes("multipart/form-data")]
    [Produces("audio/wav", "audio/mpeg", "audio/mp4", "audio/flac")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Audio([FromForm] VideoAudioForm form)
    {
        var outputType = AudioOutput(form.Format);
        var options = new AudioExtractionOptions
        {
            AudioTrackIndex = form.AudioTrackIndex, Start = MediaToolParameters.Time(form.StartSeconds),
            Duration = form.DurationSeconds is { } duration ? MediaToolParameters.Time(duration) : null
        };
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("audio" + outputType.Extension);
        await _processor.ExtractAudioAsync(input, output, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "audio" + outputType.Extension);
    }

    /// <summary>在视频指定时间范围添加文字水印。</summary>
    /// <remarks>需已安装ffmpeg/ffprobe，默认从PATH查找；文字使用系统字体，Windows优先微软雅黑，可用ToolDemos:FontFilePath覆盖。支持位置、透明度、旋转和平铺；不指定结束时间则显示至结尾。输入画面最多400万像素，编码使用CRF23/Fast，结果最多64MiB，每次原生命令最多2分钟。</remarks>
    [HttpPost("watermarks/text"), Consumes("multipart/form-data")]
    [Produces("video/mp4", "video/x-matroska", "video/webm")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TextWatermark([FromForm] VideoTextWatermarkForm form)
    {
        var text = form.Watermark(configuration);
        var outputType = VideoOutput(form.Format);
        var options = WatermarkOptions(form, form.StartSeconds, form.EndSeconds);
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("watermarked" + outputType.Extension);
        await _processor.AddTextWatermarkAsync(input, output, text, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "watermarked" + outputType.Extension);
    }

    /// <summary>在视频指定时间范围添加Logo水印。</summary>
    /// <remarks>同时上传MP4/MKV/WebM原视频及PNG/JPEG/WebP Logo。Logo可等比缩放、旋转和平铺；默认水印显示全片。需ffmpeg/ffprobe，输入画面最多400万像素，编码使用CRF23/Fast，结果最多64MiB，每次原生命令最多2分钟。</remarks>
    [HttpPost("watermarks/logo"), Consumes("multipart/form-data")]
    [Produces("video/mp4", "video/x-matroska", "video/webm")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoWatermark([FromForm] VideoLogoWatermarkForm form)
    {
        var size = form.LogoSize();
        var outputType = VideoOutput(form.Format);
        var options = WatermarkOptions(form, form.StartSeconds, form.EndSeconds);
        using var logo = new MemoryStream(await Files.ReadAsync(form.Logo, Token, UploadFileKind.Image));
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("watermarked" + outputType.Extension);
        await _processor.AddLogoWatermarkAsync(input, output, logo, size, options, Token);
        return Download(await workspace.ReadResultAsync(output, Token), outputType.Mime, "watermarked" + outputType.Extension);
    }

    private static VideoWatermarkOptions WatermarkOptions(WatermarkDemoForm form, double start, double? end) => new()
    {
        Placement = form.Placement(), Start = MediaToolParameters.Time(start),
        End = end is { } seconds ? MediaToolParameters.Time(seconds) : null
    };

    private static (string Mime, string Extension) VideoOutput(DemoVideoFormat format) => format switch
    {
        DemoVideoFormat.Mp4 => ("video/mp4", ".mp4"), DemoVideoFormat.Mkv => ("video/x-matroska", ".mkv"),
        DemoVideoFormat.Webm => ("video/webm", ".webm"), _ => throw new ArgumentException("不支持的视频输出格式。")
    };

    private static (string Mime, string Extension) AudioOutput(DemoAudioFormat format) => format switch
    {
        DemoAudioFormat.Wav => ("audio/wav", ".wav"), DemoAudioFormat.Mp3 => ("audio/mpeg", ".mp3"),
        DemoAudioFormat.M4a => ("audio/mp4", ".m4a"), DemoAudioFormat.Flac => ("audio/flac", ".flac"),
        _ => throw new ArgumentException("不支持的音频输出格式。")
    };
}
