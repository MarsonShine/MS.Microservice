using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference;
using MS.Microservice.Media.Inference.BackgroundRemoval;
using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Media.Inference.SuperResolution;
using MS.Microservice.Media.Inference.WatermarkRemoval;

namespace MS.Microservice.Lab.Application.Tools;

public sealed class ToolInferenceRuntime(IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MiganInpainter? _repair;
    private WatermarkRemover? _automatic;
    private Swin2SrUpscaler? _upscaler;
    private BiRefNetBackgroundRemover? _background;
    private bool _disposed;
    public static ImageLimits ImageLimits => new() { MaximumDimension = 4096, MaximumPixels = 4_000_000, MaximumEncodedBytes = ToolDemoFiles.MaximumFileBytes };

    public string RequiredFile(string name)
    {
        var path = configuration["ToolDemos:" + name];
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new ToolDependencyException("ToolDemos:" + name);
        return path;
    }

    public OcrOptions OcrOptions()
    {
        var directory = configuration["ToolDemos:OcrModelDirectory"];
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new ToolDependencyException("ToolDemos:OcrModelDirectory");
        return new() { PythonExecutablePath = RequiredFile("PythonExecutablePath"), ModelDirectory = directory, CpuThreads = Math.Min(2, Environment.ProcessorCount), Limits = ImageLimits };
    }

    public Task<MiganInpainter> RepairAsync(CancellationToken token) => GetRepairAsync(token);
    private async Task<MiganInpainter> GetRepairAsync(CancellationToken token) => _repair ??= await MiganInpainter.CreateAsync(
        new() { ModelPath = RequiredFile("MiganModelPath"), CpuThreads = Math.Min(2, Environment.ProcessorCount), Limits = ImageLimits }, token);

    public async Task<WatermarkRemover> AutomaticAsync(CancellationToken token) => _automatic ??= await WatermarkRemover.CreateAsync(new()
    {
        Repair = new() { ModelPath = RequiredFile("MiganModelPath"), CpuThreads = Math.Min(2, Environment.ProcessorCount), Limits = ImageLimits },
        Detector = new() { PythonExecutablePath = RequiredFile("PythonExecutablePath"), ModelPath = RequiredFile("WatermarkDetectorModelPath"), CpuThreads = Math.Min(2, Environment.ProcessorCount) }
    }, token);

    public async Task<Swin2SrUpscaler> UpscalerAsync(CancellationToken token) => _upscaler ??= await Swin2SrUpscaler.CreateAsync(
        new() { ModelPath = RequiredFile("SuperResolutionModelPath"), CpuThreads = Math.Min(2, Environment.ProcessorCount), Limits = ImageLimits }, token);

    public async Task<BiRefNetBackgroundRemover> BackgroundAsync(CancellationToken token) => _background ??= await BiRefNetBackgroundRemover.CreateAsync(
        new() { ModelPath = RequiredFile("BackgroundRemovalModelPath"), Limits = ImageLimits }, token);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        // All inference demos share one gate, including separate requests and different model types.
        await _gate.WaitAsync(token);
        try { ObjectDisposedException.ThrowIf(_disposed, this); return await action(token); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            IAsyncDisposable?[] models = [_repair, _automatic, _upscaler, _background];
            foreach (var model in models)
                if (model is not null)
                    try { await model.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("媒体推理会话释放失败。", failures);
        }
        finally { _gate.Release(); }
    }
}
