using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class InferenceToolsApiTests
{
    [Theory]
    [InlineData("ocr")]
    [InlineData("tables")]
    [InlineData("watermarks/remove")]
    [InlineData("repair")]
    [InlineData("background/remove")]
    [InlineData("upscale")]
    public async Task UnconfiguredModelsGiveAnActionableResponse(string endpoint)
    {
        using var host = new ToolDemoTestHost();
        using var form = ImageForm(Png(8, 8, SKColors.Blue));
        if (endpoint == "repair") AddImage(form, Png(8, 8, SKColors.Black), "Mask");
        using var response = await host.Client.PostAsync("/api/lab/tools/inference/" + endpoint, form);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ToolDemos:", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("ocr", false, false)]
    [InlineData("repair", false, true)]
    [InlineData("repair", true, false)]
    [InlineData("videos/repair", false, false)]
    public async Task RequiredUploadsAreValidatedBeforeModelsAreOpened(string endpoint, bool image, bool mask)
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        if (image) AddImage(form, Png(8, 8, SKColors.Blue), "File");
        if (mask) AddImage(form, Png(8, 8, SKColors.Black), "Mask");
        using var response = await host.Client.PostAsync("/api/lab/tools/inference/" + endpoint, form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LabToolNativeFact]
    public async Task RealOcrAndRuledTableReturnRecognizedTextAndCells()
    {
        using var host = new ToolDemoTestHost(NativeConfiguration());
        using var ocrForm = ImageForm(await File.ReadAllBytesAsync(Fixture("Ocr", "bilingual.png")));
        using var ocrResponse = await host.Client.PostAsync("/api/lab/tools/inference/ocr", ocrForm);
        ocrResponse.EnsureSuccessStatusCode();
        using var ocr = JsonDocument.Parse(await ocrResponse.Content.ReadAsStringAsync());
        Assert.Contains(ocr.RootElement.GetProperty("regions").EnumerateArray(), region => region.GetProperty("text").GetString() == "中文识别测试");
        using var tableForm = ImageForm(await File.ReadAllBytesAsync(Fixture("Tables", "bilingual-grid.png")));
        using var tableResponse = await host.Client.PostAsync("/api/lab/tools/inference/tables", tableForm);
        tableResponse.EnsureSuccessStatusCode();
        using var table = JsonDocument.Parse(await tableResponse.Content.ReadAsStringAsync());
        Assert.NotEmpty(table.RootElement.GetProperty("tables").EnumerateArray());
        Assert.True(table.RootElement.GetProperty("reviewRecommended").GetBoolean());
    }

    [LabToolNativeFact]
    public async Task RealImageRepairAndTwofoldEnhancementReturnUsablePngs()
    {
        using var host = new ToolDemoTestHost(NativeConfiguration());
        var image = Png(16, 12, SKColors.Blue);
        using var repairForm = ImageForm(image);
        using var mask = new SKBitmap(16, 12);
        mask.Erase(SKColors.Black);
        mask.SetPixel(8, 6, SKColors.White);
        AddImage(repairForm, Encode(mask), "Mask");
        using var repair = await host.Client.PostAsync("/api/lab/tools/inference/repair", repairForm);
        repair.EnsureSuccessStatusCode();
        using var repaired = SKBitmap.Decode(await repair.Content.ReadAsByteArrayAsync());
        Assert.Equal((16, 12), (repaired.Width, repaired.Height));
        Assert.Equal(SKColors.Blue, repaired.GetPixel(0, 0));
        using var upscaleForm = ImageForm(image);
        using var upscale = await host.Client.PostAsync("/api/lab/tools/inference/upscale", upscaleForm);
        upscale.EnsureSuccessStatusCode();
        using var enlarged = SKBitmap.Decode(await upscale.Content.ReadAsByteArrayAsync());
        Assert.Equal((32, 24), (enlarged.Width, enlarged.Height));
    }

    [LabToolNativeFact]
    public async Task RealAutomaticWatermarkRemovalReturnsImageMaskAndReviewReport()
    {
        using var host = new ToolDemoTestHost(NativeConfiguration());
        using var form = ImageForm(await File.ReadAllBytesAsync(Fixture("WatermarkRemoval", "positive-repeated.png")));
        using var response = await host.Client.PostAsync("/api/lab/tools/inference/watermarks/remove", form);
        response.EnsureSuccessStatusCode();
        using var content = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("result.png"));
        Assert.NotNull(archive.GetEntry("mask.png"));
        using var reportStream = archive.GetEntry("report.json")!.Open();
        using var report = await JsonDocument.ParseAsync(reportStream);
        Assert.True(report.RootElement.GetProperty("ReviewRecommended").GetBoolean());
        Assert.True(report.RootElement.GetProperty("Detection").GetProperty("SelectedPixelCount").GetInt32() > 0);
    }

    [LabToolNativeFact]
    public async Task RealBackgroundRemovalProducesForegroundWithTransparentPixels()
    {
        using var host = new ToolDemoTestHost(NativeConfiguration());
        using var form = ImageForm(await File.ReadAllBytesAsync(Fixture("BackgroundRemoval", "grace-hopper.jpg")), "portrait.jpg", "image/jpeg");
        using var response = await host.Client.PostAsync("/api/lab/tools/inference/background/remove", form);
        response.EnsureSuccessStatusCode();
        using var png = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(png.Pixels, pixel => pixel.Alpha > 0);
        Assert.Contains(png.Pixels, pixel => pixel.Alpha == 0);
    }

    [LabToolNativeFact]
    public async Task RealAutomaticVideoRepairReturnsAnAudioPreservingPreviewBundle()
    {
        using var host = new ToolDemoTestHost(NativeConfiguration());
        var source = Path.Combine(host.Root, "source.mp4");
        using (var process = new Process { StartInfo = new("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } })
        {
            foreach (var argument in new[] { "-v", "error", "-y", "-loop", "1", "-framerate", "2", "-i", Fixture("WatermarkRemoval", "positive-repeated.png"),
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=24000", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-threads", "1", "-shortest", source })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.True(process.ExitCode == 0, await errors);
        }
        using var form = new MultipartFormDataContent();
        var video = new ByteArrayContent(await File.ReadAllBytesAsync(source));
        video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(video, "File", "source.mp4");
        using var response = await host.Client.PostAsync("/api/lab/tools/inference/videos/repair", form);
        response.EnsureSuccessStatusCode();
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count(entry => entry.Name.EndsWith(".mkv", StringComparison.Ordinal)));
        Assert.True(archive.Entries.Count(entry => entry.Name.EndsWith(".png", StringComparison.Ordinal)) >= 3);
    }

    private static Dictionary<string, string?> NativeConfiguration()
    {
        var root = RepositoryRoot();
        var models = Path.Combine(root, ".tools", "media-models");
        return new()
        {
            ["ToolDemos:PythonExecutablePath"] = Environment.GetEnvironmentVariable("MS_MEDIA_OCR_PYTHON") ?? Path.Combine(root, ".tools", "media-inference-env", "Scripts", "python.exe"),
            ["ToolDemos:OcrModelDirectory"] = Environment.GetEnvironmentVariable("MS_MEDIA_OCR_MODELS") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS.Microservice", "models", "ocr-v5"),
            ["ToolDemos:MiganModelPath"] = Path.Combine(models, "migan_pipeline_v2.onnx"),
            ["ToolDemos:WatermarkDetectorModelPath"] = Path.Combine(models, "segmenter_repeated_text.pth"),
            ["ToolDemos:SuperResolutionModelPath"] = Path.Combine(models, "swin2sr-x2.onnx"),
            ["ToolDemos:BackgroundRemovalModelPath"] = Path.Combine(models, "birefnet-lite.onnx")
        };
    }

    private static string Fixture(string feature, string file) => Path.Combine(RepositoryRoot(), "MS.Microservice.Media", "test", "MS.Microservice.Media.Inference.Tests", feature, "Fixtures", file);
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(file)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS.Microservice.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
    private static MultipartFormDataContent ImageForm(byte[] image, string name = "source.png", string mime = "image/png")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(content, "File", name);
        return form;
    }
    private static void AddImage(MultipartFormDataContent form, byte[] image, string field)
    {
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, field, field + ".png");
    }
    private static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return Encode(bitmap);
    }
    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}

internal sealed class LabToolNativeFactAttribute : FactAttribute
{
    public LabToolNativeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LAB_TOOL_NATIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_LAB_TOOL_NATIVE_TESTS=true after preparing the fixed local models, Python and FFmpeg.";
    }
}
