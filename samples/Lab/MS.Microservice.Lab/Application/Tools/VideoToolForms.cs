using System.ComponentModel.DataAnnotations;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Lab.Application.Tools;

public enum DemoVideoFormat { Mp4, Mkv, Webm }
public enum DemoAudioFormat { Wav, Mp3, M4a, Flac }

public class VideoUploadForm
{
    /// <summary>本地上传的MP4、MKV或WebM视频，最多20MiB；不接受URL或服务器路径。</summary>
    [Required] public IFormFile? File { get; set; }
}

public sealed class VideoTransformForm : VideoUploadForm
{
    /// <summary>输出容器：0=Mp4、1=Mkv、2=Webm。</summary>
    public DemoVideoFormat Format { get; set; } = DemoVideoFormat.Mp4;
    /// <summary>开始时间，单位为秒，默认0。</summary>
    [Range(0, double.MaxValue)] public double StartSeconds { get; set; }
    /// <summary>保留时长，单位为秒；省略保留至结尾。</summary>
    [Range(double.Epsilon, double.MaxValue)] public double? DurationSeconds { get; set; }
    /// <summary>目标宽度，与Height同时填写；输入输出均限4096边长、400万像素。</summary>
    [Range(1, 4096)] public int? Width { get; set; }
    /// <summary>目标高度，与Width同时填写。</summary>
    [Range(1, 4096)] public int? Height { get; set; }
    /// <summary>缩放模式：0=Contain等比包含并补黑边、1=Cover等比裁满、2=Stretch拉伸。</summary>
    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.Contain;
    /// <summary>方向修正后画面裁剪起点X；四个Crop参数需全部填写。</summary>
    [Range(0, 4096)] public int? CropX { get; set; }
    /// <summary>裁剪起点Y。</summary>
    [Range(0, 4096)] public int? CropY { get; set; }
    /// <summary>裁剪宽度。</summary>
    [Range(1, 4096)] public int? CropWidth { get; set; }
    /// <summary>裁剪高度。</summary>
    [Range(1, 4096)] public int? CropHeight { get; set; }
    /// <summary>顺时针旋转：0、90、180或270度。</summary>
    public int RotationDegrees { get; set; }
    /// <summary>是否水平翻转。</summary>
    public bool FlipHorizontal { get; set; }
    /// <summary>是否垂直翻转。</summary>
    public bool FlipVertical { get; set; }
    /// <summary>视频质量CRF，0到51，越小质量越高、文件通常越大。</summary>
    [Range(0, 51)] public int ConstantRateFactor { get; set; } = 23;
    /// <summary>编码速度：0=Fast、1=Balanced、2=Slow。</summary>
    public VideoEncodingSpeed Speed { get; set; } = VideoEncodingSpeed.Fast;
}

public sealed class VideoConcatenateForm
{
    /// <summary>按播放顺序上传1至4段MP4、MKV或WebM；每份最多20MiB，整个请求最多25MiB。</summary>
    [Required, MinLength(1), MaxLength(4)] public List<IFormFile> Files { get; set; } = [];
    /// <summary>输出容器：0=Mp4、1=Mkv、2=Webm。</summary>
    public DemoVideoFormat Format { get; set; } = DemoVideoFormat.Mp4;
    /// <summary>统一画布宽度；与Height同时填写。不填使用第一段尺寸，奇数边长补齐为偶数。</summary>
    [Range(1, 4096)] public int? Width { get; set; }
    /// <summary>统一画布高度；与Width同时填写。</summary>
    [Range(1, 4096)] public int? Height { get; set; }
    /// <summary>统一帧率，大于0且不超过240；不填使用第一段帧率。</summary>
    [Range(double.Epsilon, 240)] public double? FrameRate { get; set; }
    /// <summary>视频质量CRF，0到51，越小质量越高。</summary>
    [Range(0, 51)] public int ConstantRateFactor { get; set; } = 23;
    /// <summary>编码速度：0=Fast、1=Balanced、2=Slow。</summary>
    public VideoEncodingSpeed Speed { get; set; } = VideoEncodingSpeed.Fast;
}

public sealed class VideoFrameForm : VideoUploadForm
{
    /// <summary>截帧时间，单位为秒，必须早于视频结尾。</summary>
    [Range(0, double.MaxValue)] public double TimestampSeconds { get; set; }
    /// <summary>图片格式：0=Png、1=Jpeg、2=Webp。</summary>
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    /// <summary>JPEG/WebP质量，0到100。</summary>
    [Range(0, 100)] public int Quality { get; set; } = 90;
    /// <summary>可选截图宽度，与Height同时填写，按比例包含。</summary>
    [Range(1, 4096)] public int? Width { get; set; }
    /// <summary>可选截图高度，与Width同时填写。</summary>
    [Range(1, 4096)] public int? Height { get; set; }
}

public sealed class VideoAudioForm : VideoUploadForm
{
    /// <summary>音频格式：0=Wav（PCM16）、1=Mp3、2=M4a（AAC）、3=Flac。</summary>
    public DemoAudioFormat Format { get; set; } = DemoAudioFormat.Wav;
    /// <summary>音轨序号，从0开始；仅在音频轨道之间计数。</summary>
    [Range(0, 100)] public int AudioTrackIndex { get; set; }
    /// <summary>开始时间，单位为秒。</summary>
    [Range(0, double.MaxValue)] public double StartSeconds { get; set; }
    /// <summary>提取时长，单位为秒；省略保留至该音轨结尾。</summary>
    [Range(double.Epsilon, double.MaxValue)] public double? DurationSeconds { get; set; }
}

public sealed class VideoTextWatermarkForm : TextWatermarkDemoForm
{
    /// <summary>MP4、MKV或WebM原视频，最多20MiB、400万像素。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>输出容器：0=Mp4、1=Mkv、2=Webm。</summary>
    public DemoVideoFormat Format { get; set; } = DemoVideoFormat.Mp4;
    /// <summary>水印开始时间，秒。</summary>
    [Range(0, double.MaxValue)] public double StartSeconds { get; set; }
    /// <summary>水印结束时间，秒，必须大于开始时间；省略显示至结尾。</summary>
    [Range(double.Epsilon, double.MaxValue)] public double? EndSeconds { get; set; }
}

public sealed class VideoLogoWatermarkForm : LogoWatermarkDemoForm
{
    /// <summary>MP4、MKV或WebM原视频，最多20MiB、400万像素。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>输出容器：0=Mp4、1=Mkv、2=Webm。</summary>
    public DemoVideoFormat Format { get; set; } = DemoVideoFormat.Mp4;
    /// <summary>水印开始时间，秒。</summary>
    [Range(0, double.MaxValue)] public double StartSeconds { get; set; }
    /// <summary>水印结束时间，秒，必须大于开始时间；省略显示至结尾。</summary>
    [Range(double.Epsilon, double.MaxValue)] public double? EndSeconds { get; set; }
}
