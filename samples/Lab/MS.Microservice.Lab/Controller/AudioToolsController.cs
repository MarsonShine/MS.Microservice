using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Infrastructure.Common.NAudio;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using NAudio.Wave;

namespace MS.Microservice.Lab.Controller;

/// <summary>音频信息、编辑、合并、混音和转码示例。</summary>
[LabOnly]
[Route("api/lab/tools/audio")]
public sealed class AudioToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    private static readonly string[] InputExtensions = [".wav", ".mp3"];

    /// <summary>读取音频时长、格式、采样率和声道信息。</summary>
    /// <remarks>上传 WAV 或 MP3，返回 JSON。MP3 解码需 Windows ACM／Media Foundation；不会返回宿主文件路径。</remarks>
    [HttpPost("info"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType(typeof(AudioToolInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> Info([FromForm] AudioInputForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var path = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var info = AudioProcessorExtensions.GetAudioFileInfo(path);
        return Ok(new AudioToolInfo
        {
            Format = AudioFileFormatDetector.DetectActualFormat(path).ToString(), DurationSeconds = info.Duration.TotalSeconds,
            SampleRate = info.SampleRate, Channels = info.Channels, BitsPerSample = info.BitsPerSample, FileBytes = info.FileSize
        });
    }

    /// <summary>剪辑音频片段，并调整音量和淡入淡出。</summary>
    /// <remarks>上传 WAV／MP3，填写秒数后下载新音频。保留原采样率与声道；WAV 输出为 32 位浮点。MP3 编解码需 Windows 与匹配架构的 LAME。</remarks>
    [HttpPost("edit"), Consumes("multipart/form-data"), Produces("audio/wav", "audio/mpeg")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Edit([FromForm] AudioEditForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("edited" + Extension(form.Format));
        await new AudioEditor().EditAsync(input, output, new()
        {
            Start = TimeSpan.FromSeconds(form.StartSeconds), Duration = form.DurationSeconds is { } duration ? TimeSpan.FromSeconds(duration) : null,
            Gain = form.Gain, FadeIn = TimeSpan.FromSeconds(form.FadeInSeconds), FadeOut = TimeSpan.FromSeconds(form.FadeOutSeconds)
        }, cancellationToken: Token);
        return Download(await workspace.ReadResultAsync(output, Token), Mime(form.Format), "edited" + Extension(form.Format));
    }

    /// <summary>按上传顺序合并多个音频文件。</summary>
    /// <remarks>选择 2–8 个 WAV／MP3，下载合并结果。静音会加在每个输入之后，包括最后一个；原合并 API 不支持处理中途取消，涉及转码时需 Windows 编解码器。</remarks>
    [HttpPost("combine"), Consumes("multipart/form-data"), Produces("audio/wav", "audio/mpeg")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Combine([FromForm] AudioCombineForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var inputs = await SaveInputsAsync(workspace, form.Files);
        var output = workspace.NewPath("combined" + Extension(form.Format));
        await new AudioProcessor().CombineAudioFilesAsync(inputs, output, new() { SortByFileName = false, SilenceDuration = form.SilenceSeconds });
        return Download(await workspace.ReadResultAsync(output, Token), Mime(form.Format), "combined" + Extension(form.Format));
    }

    /// <summary>将多个音轨同时混合到一份音频。</summary>
    /// <remarks>上传 2–8 个 WAV／MP3，下载同时播放各输入的混音结果。默认将单声道转为立体声；依赖 Windows 编解码路径，原混音 API 不支持处理中途取消。</remarks>
    [HttpPost("mix"), Consumes("multipart/form-data"), Produces("audio/wav", "audio/mpeg")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mix([FromForm] AudioFilesForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var inputs = await SaveInputsAsync(workspace, form.Files);
        var output = workspace.NewPath("mixed" + Extension(form.Format));
        await new AudioProcessor().MixAudioFilesAsync(inputs, output);
        return Download(await workspace.ReadResultAsync(output, Token), Mime(form.Format), "mixed" + Extension(form.Format));
    }

    /// <summary>转换音频格式、采样率与声道数。</summary>
    /// <remarks>上传 WAV／MP3 并指定目标格式后下载结果，PCM 目标位数为 16。需 Windows 重采样／解码支持；MP3 编码另需 LAME，原转换 API 不支持处理中途取消。</remarks>
    [HttpPost("convert"), Consumes("multipart/form-data"), Produces("audio/wav", "audio/mpeg")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Convert([FromForm] AudioConvertForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("converted" + Extension(form.Format));
        await new AudioProcessor().ConvertAudioFormatAsync(input, output, new() { TargetFormat = new WaveFormat(form.SampleRate, 16, form.Channels) });
        return Download(await workspace.ReadResultAsync(output, Token), Mime(form.Format), "converted" + Extension(form.Format));
    }

    /// <summary>将单声道音频转换为立体声。</summary>
    /// <remarks>上传单声道 WAV／MP3，下载固定双声道音频，目标采样率采用 SampleRate。需 Windows 编解码支持，MP3 输出另需 LAME。</remarks>
    [HttpPost("mono-to-stereo"), Consumes("multipart/form-data"), Produces("audio/wav", "audio/mpeg")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> MonoToStereo([FromForm] AudioTranscodeForm form)
    {
        using var workspace = Files.CreateWorkspace();
        var input = await workspace.SaveAsync(form.File, InputExtensions, Token);
        var output = workspace.NewPath("stereo" + Extension(form.Format));
        await new AudioProcessor().ConvertMonoToStereoAsync(input, output, new() { TargetFormat = new WaveFormat(form.SampleRate, 16, 2) });
        return Download(await workspace.ReadResultAsync(output, Token), Mime(form.Format), "stereo" + Extension(form.Format));
    }

    private async Task<List<string>> SaveInputsAsync(ToolDemoWorkspace workspace, IReadOnlyList<IFormFile> inputs)
    {
        if (inputs.Count is < 2 or > 8) throw new ArgumentException("请选择 2–8 个音频文件。");
        var paths = new List<string>();
        foreach (var input in inputs) paths.Add(await workspace.SaveAsync(input, InputExtensions, Token));
        return paths;
    }

    private static string Extension(AudioToolFormat format) => format switch
    { AudioToolFormat.Wav => ".wav", AudioToolFormat.Mp3 => ".mp3", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
    private static string Mime(AudioToolFormat format) => format == AudioToolFormat.Mp3 ? "audio/mpeg" : "audio/wav";
}
