namespace MS.Microservice.Media.Inference.VideoWatermarkRemoval;

public sealed record VideoRepairLimits
{
    public long MaximumInputBytes { get; init; } = 256L * 1024 * 1024;
    public long MaximumOutputBytes { get; init; } = 512L * 1024 * 1024;
    public int MaximumFrames { get; init; } = 300;
    public int MaximumFramePixels { get; init; } = 1920 * 1080;
    public int MaximumFrameDimension { get; init; } = 8192;
    public long MaximumTotalPixels { get; init; } = 256_000_000;
    public double MaximumDurationSeconds { get; init; } = 120;
    public int MaximumAudioTracks { get; init; } = 8;
    public long MaximumAudioSampleValues { get; init; } = 50_000_000;
    public int MaximumMaskKeyframes { get; init; } = 512;
    public int MaximumRectanglesPerKeyframe { get; init; } = 128;
    public int MaximumMaskRectangles { get; init; } = 65_536;

    internal void Validate()
    {
        if (MaximumInputBytes < 1 || MaximumOutputBytes < 1 || MaximumFrames < 1 || MaximumFramePixels < 1
            || MaximumFrameDimension is < 1 or > 32766 || MaximumTotalPixels < 1 || !double.IsFinite(MaximumDurationSeconds) || MaximumDurationSeconds <= 0
            || MaximumAudioTracks < 0 || MaximumAudioSampleValues < 1
            || MaximumMaskKeyframes < 1 || MaximumRectanglesPerKeyframe < 1 || MaximumMaskRectangles < 1)
            throw new ArgumentException("Video, audio and byte limits must be finite and positive; the audio track limit may be zero.");
    }
}

/// <summary>Reliability gates for image-model repairs assisted by adjacent-frame optical flow; these thresholds are not accuracy calibration.</summary>
public sealed record VideoFlowOptions
{
    public double MaximumForwardBackwardError { get; init; } = 1.5;
    public double MaximumKnownRegionError { get; init; } = 32;
    public double MinimumTexturedFraction { get; init; } = 0.01;
    public double MinimumGradient { get; init; } = 4;
    public double GeneratedNeighborWeight { get; init; } = 0.25;

    internal void Validate()
    {
        double[] values = [MaximumForwardBackwardError, MaximumKnownRegionError, MinimumTexturedFraction, MinimumGradient, GeneratedNeighborWeight];
        if (values.Any(value => !double.IsFinite(value) || value <= 0)
            || MaximumKnownRegionError > 255 || MinimumTexturedFraction > 1 || GeneratedNeighborWeight > 1)
            throw new ArgumentException("Flow thresholds must be finite and positive; fractions and neighbor weight cannot exceed one.");
    }
}

public sealed record VideoWatermarkRemovalOptions
{
    public required string PythonExecutablePath { get; init; }
    public required string MiganModelPath { get; init; }
    public string FfmpegExecutablePath { get; init; } = "ffmpeg";
    public int CpuThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public VideoRepairLimits Limits { get; init; } = new();
    public VideoFlowOptions Flow { get; init; } = new();

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PythonExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(MiganModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(FfmpegExecutablePath);
        ArgumentNullException.ThrowIfNull(Limits);
        ArgumentNullException.ThrowIfNull(Flow);
        if (!Path.IsPathFullyQualified(PythonExecutablePath)) throw new ArgumentException("Configure an absolute, trusted Python executable.");
        if (!File.Exists(PythonExecutablePath)) throw new FileNotFoundException("The configured Python executable does not exist.", PythonExecutablePath);
        if (!File.Exists(MiganModelPath)) throw new FileNotFoundException("The configured MI-GAN model does not exist.", MiganModelPath);
        if (CpuThreads is < 1 or > 64 || ProcessTimeout <= TimeSpan.Zero || ProcessTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentException("Use 1–64 CPU threads and a finite positive process timeout.");
        Limits.Validate();
        Flow.Validate();
    }
}

public sealed record VideoMaskRectangle(int X, int Y, int Width, int Height);
public sealed record VideoMaskKeyframe(TimeSpan At, IReadOnlyList<VideoMaskRectangle> Rectangles);

/// <summary>Use one static binary PNG or ordered rectangle keyframes. Coordinates refer to the display-normalized frame.</summary>
public sealed record VideoWatermarkRemovalRequest
{
    public string? StaticMaskPath { get; init; }
    public IReadOnlyList<VideoMaskKeyframe>? RectangleKeyframes { get; init; }

    internal VideoMaskRequestSnapshot Snapshot(VideoRepairLimits? limits = null)
    {
        limits ??= new VideoRepairLimits();
        limits.Validate();
        if ((StaticMaskPath is null) == (RectangleKeyframes is null))
            throw new ArgumentException("Supply exactly one static mask or rectangle keyframe sequence.");
        if (StaticMaskPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(StaticMaskPath);
            var path = Path.GetFullPath(StaticMaskPath);
            if (!File.Exists(path)) throw new FileNotFoundException("The static mask does not exist.", path);
            return new(path, null);
        }
        var count = RectangleKeyframes!.Count;
        if (count < 1 || count > limits.MaximumMaskKeyframes)
            throw new ArgumentException("The mask keyframe count exceeds its positive request budget.");
        var frames = new VideoMaskKeyframeSnapshot[count];
        var total = 0;
        for (var index = 0; index < count; index++)
        {
            var frame = RectangleKeyframes[index] ?? throw new ArgumentException("Mask keyframes cannot be null.");
            var rectangles = frame.Rectangles ?? throw new ArgumentException("Keyframe rectangles cannot be null.");
            var rectangleCount = rectangles.Count;
            if (rectangleCount < 1 || rectangleCount > limits.MaximumRectanglesPerKeyframe || rectangleCount > limits.MaximumMaskRectangles - total)
                throw new ArgumentException("The per-keyframe or total mask rectangle count exceeds its positive request budget.");
            total += rectangleCount;
            var copied = new VideoMaskRectangle[rectangleCount];
            for (var rectangleIndex = 0; rectangleIndex < rectangleCount; rectangleIndex++)
            {
                var rect = rectangles[rectangleIndex];
                if (rect is null || rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
                    throw new ArgumentException("Mask rectangles need nonnegative coordinates and positive dimensions.");
                copied[rectangleIndex] = rect;
            }
            if (index == 0 && frame.At != TimeSpan.Zero || index > 0 && (frame.At.Ticks <= frames[index - 1].TimeTicks || rectangleCount != frames[0].Rectangles.Length))
                throw new ArgumentException("Keyframe times must increase; corresponding rectangle counts must match and coordinates must be nonnegative with positive sizes.");
            frames[index] = new(frame.At.Ticks, copied);
        }
        return new(null, frames);
    }
}

internal sealed record VideoMaskRequestSnapshot(string? StaticMaskPath, VideoMaskKeyframeSnapshot[]? RectangleKeyframes);
internal sealed record VideoMaskKeyframeSnapshot(long TimeTicks, VideoMaskRectangle[] Rectangles);

/// <summary>Samples preserve interleaved channel order; source speaker roles are recorded here because Matroska PCM may omit layout labels.</summary>
public sealed record VideoAudioResult(int StreamIndex, string SourceCodec, int SampleRate, string Layout, int Channels,
    IReadOnlyList<string> ChannelRoles, long Samples, double FirstSeconds, string Float32Sha256);
public sealed record VideoWatermarkRemovalResult(string OriginalPath, string OriginalSha256, string OutputDirectory,
    int Width, int Height, int FrameCount, long SelectedPixelCount, long EditedPixelCount, long FlowContributedPixelCount,
    double FirstVideoSeconds, double VideoDurationSeconds, long OutputBytes, IReadOnlyList<VideoAudioResult> Audio,
    bool ReviewRecommended, IReadOnlyList<string> Warnings);
