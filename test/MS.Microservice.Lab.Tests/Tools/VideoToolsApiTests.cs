using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MS.Microservice.Media.Video;
using SkiaSharp;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class VideoToolsApiTests
{
    [MediaDemoFfmpegFact]
    public async Task UploadedVideoCanBeProbedWithRealAudioAndTiming()
    {
        using var host = new ToolDemoTestHost();
        var source = await SourceAsync(host.Root);

        var info = await ProbeAsync(host, source);

        Assert.Equal(80, info.Width);
        Assert.Equal(60, info.Height);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.95, 2.2);
        Assert.InRange(info.FrameRate!.Value, 9.9, 10.1);
    }

    [MediaDemoFfmpegFact]
    public async Task EditedVideoCanBeReuploadedAndCapturedAtItsNewTimeline()
    {
        using var host = new ToolDemoTestHost();
        var source = await SourceAsync(host.Root);
        using var form = VideoForm(source);
        Add(form, "StartSeconds", "0.5", "DurationSeconds", "1", "Width", "40", "Height", "30");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/process", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        var edited = await response.Content.ReadAsByteArrayAsync();
        var info = await ProbeAsync(host, edited);
        Assert.Equal(40, info.Width);
        Assert.Equal(30, info.Height);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 0.9, 1.2);
        using var frame = await FrameAsync(host, edited, "0.75");
        Assert.Equal(40, frame.Width);
        Assert.Equal(30, frame.Height);
        AssertBlue(frame.GetPixel(20, 15));
    }

    [MediaDemoFfmpegFact]
    public async Task AudioExtractionReturnsActualPcmSamplesForTheSelectedInterval()
    {
        using var host = new ToolDemoTestHost();
        var source = await SourceAsync(host.Root);
        using var form = VideoForm(source);
        Add(form, "Format", "0", "StartSeconds", "0.25", "DurationSeconds", "0.5");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/audio", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        AssertPcmWave(await response.Content.ReadAsByteArrayAsync());
    }

    [MediaDemoFfmpegFact]
    public async Task ConcatenationPreservesTheOrderOfBothUploadedVideos()
    {
        using var host = new ToolDemoTestHost();
        var first = await SourceAsync(host.Root);
        var second = await SourceAsync(host.Root, reverse: true);
        using var form = new MultipartFormDataContent();
        AddVideo(form, first, "Files", "first.mp4");
        AddVideo(form, second, "Files", "second.mp4");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/concatenate", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var joined = await response.Content.ReadAsByteArrayAsync();
        var info = await ProbeAsync(host, joined);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 3.9, 4.3);
        using var firstFrame = await FrameAsync(host, joined, "0.25");
        using var secondFrame = await FrameAsync(host, joined, "2.25");
        using var lastFrame = await FrameAsync(host, joined, "3.25");
        AssertRed(firstFrame.GetPixel(40, 30));
        AssertBlue(secondFrame.GetPixel(40, 30));
        AssertRed(lastFrame.GetPixel(40, 30));
    }

    [MediaDemoFfmpegFact]
    public async Task TextWatermarkAppearsInsideItsConfiguredVideoTimeWindow()
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?> { ["ToolDemos:FontFilePath"] = FontPath() });
        var source = await SourceAsync(host.Root);
        using var form = VideoForm(source);
        Add(form, "Text", "DEMO", "FontSize", "16", "Anchor", "4", "MarginX", "2", "MarginY", "2", "Opacity", "1", "StartSeconds", "0.5", "EndSeconds", "1.5");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/watermarks/text", form);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            response.StatusCode == HttpStatusCode.OK ? null : $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var marked = await response.Content.ReadAsByteArrayAsync();
        using var before = await FrameAsync(host, marked, "0.25");
        using var during = await FrameAsync(host, marked, "0.75");
        Assert.Equal(0, LightPixels(before));
        Assert.True(LightPixels(during) > 10);
        Assert.True((await ProbeAsync(host, marked)).HasAudio);
    }

    [MediaDemoFfmpegFact]
    public async Task TextWatermarkRejectsStampThatCannotFitDefaultMargins()
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?> { ["ToolDemos:FontFilePath"] = FontPath() });
        var source = await SourceAsync(host.Root);
        using var form = VideoForm(source);
        Add(form, "Text", "DEMO", "FontSize", "16", "Anchor", "4", "Opacity", "1");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/watermarks/text", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("margins", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [MediaDemoFfmpegFact]
    public async Task LogoWatermarkUsesUploadedLogoOnlyInsideItsVideoTimeWindow()
    {
        using var host = new ToolDemoTestHost();
        var source = await SourceAsync(host.Root);
        using var form = VideoForm(source);
        using var bitmap = new SKBitmap(12, 12);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Lime);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var logo = new ByteArrayContent(encoded.ToArray());
        logo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(logo, "Logo", "logo.png");
        Add(form, "Anchor", "0", "MarginX", "0", "MarginY", "0", "Opacity", "1", "StartSeconds", "0.5", "EndSeconds", "1.5");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/watermarks/logo", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var marked = await response.Content.ReadAsByteArrayAsync();
        using var before = await FrameAsync(host, marked, "0.25");
        using var during = await FrameAsync(host, marked, "0.75");
        using var after = await FrameAsync(host, marked, "1.75");
        AssertRed(before.GetPixel(5, 5));
        var pixel = during.GetPixel(5, 5);
        Assert.True(pixel.Green > 220 && pixel.Red < 30 && pixel.Blue < 30);
        AssertRed(during.GetPixel(60, 40));
        AssertBlue(after.GetPixel(5, 5));
    }

    private static async Task<byte[]> SourceAsync(string root, bool reverse = false)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".mp4");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true }
        };
        string[] arguments =
        [
            "-v", "error", "-nostdin", "-f", "lavfi", "-i", $"color=c={(reverse ? "blue" : "red")}:s=80x60:r=10:d=1",
            "-f", "lavfi", "-i", $"color=c={(reverse ? "red" : "blue")}:s=80x60:r=10:d=1",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
            "-filter_complex_threads", "1", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]",
            "-map", "[v]", "-map", "2:a", "-c:v", "libx264", "-preset", "ultrafast", "-threads", "1",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path
        ];
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var diagnostics = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await diagnostics;
            throw;
        }
        Assert.True(process.ExitCode == 0, await diagnostics);
        return await File.ReadAllBytesAsync(path);
    }

    private static MultipartFormDataContent VideoForm(byte[] source)
    {
        var form = new MultipartFormDataContent();
        AddVideo(form, source, "File", "video.mp4");
        return form;
    }

    private static void AddVideo(MultipartFormDataContent form, byte[] source, string name, string filename)
    {
        var file = new ByteArrayContent(source);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(file, name, filename);
    }

    private static void Add(MultipartFormDataContent form, params string[] values)
    {
        for (var index = 0; index < values.Length; index += 2)
            form.Add(new StringContent(values[index + 1]), values[index]);
    }

    private static async Task<VideoInfo> ProbeAsync(ToolDemoTestHost host, byte[] video)
    {
        using var form = VideoForm(video);
        using var response = await host.Client.PostAsync("/api/lab/tools/videos/probe", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VideoInfo>())!;
    }

    private static async Task<SKBitmap> FrameAsync(ToolDemoTestHost host, byte[] video, string seconds)
    {
        using var form = VideoForm(video);
        Add(form, "TimestampSeconds", seconds);
        using var response = await host.Client.PostAsync("/api/lab/tools/videos/frame", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync()) ?? throw new InvalidDataException("Missing captured PNG.");
    }

    private static int LightPixels(SKBitmap image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var color = image.GetPixel(x, y);
                // Lossy 4:2:0 mixes glyph edges with the background; saturated source frames stay below this contrast.
                if (color.Red > byte.MaxValue / 2 && color.Green > byte.MaxValue / 2 && color.Blue > byte.MaxValue / 2) count++;
            }
        return count;
    }

    private static string FontPath()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidates = [Path.Combine(fonts, "arial.ttf"), Path.Combine(fonts, "segoeui.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"];
        return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("The video watermark HTTP test needs an installed Arial/Segoe UI/DejaVu Sans font.");
    }

    private static void AssertRed(SKColor pixel) => Assert.True(pixel.Red > 220 && pixel.Green < 30 && pixel.Blue < 30);
    private static void AssertBlue(SKColor pixel) => Assert.True(pixel.Blue > 220 && pixel.Red < 30 && pixel.Green < 30);

    private static void AssertPcmWave(byte[] bytes)
    {
        Assert.True(bytes.Length >= 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        var foundFormat = false;
        var foundData = false;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4)));
            Assert.InRange(size, 0, bytes.Length - offset - 8);
            var chunk = bytes.AsSpan(offset + 8, size);
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            if (id == "fmt ")
            {
                Assert.True(size >= 16);
                Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(chunk));
                Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]));
                Assert.Equal(48_000, BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]));
                Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]));
                foundFormat = true;
            }
            if (id == "data")
            {
                Assert.InRange(size / 96_000d, 0.45, 0.55);
                Assert.True(chunk.ContainsAnyExcept((byte)0));
                foundData = true;
            }
            offset += 8 + size + size % 2;
        }
        Assert.True(foundFormat && foundData);
    }
}
