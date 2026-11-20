using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MS.Microservice.Documents.Pdf;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Documents.Rendering.Tests;

[Collection("Rendering native")]
public sealed class HtmlPdfRendererTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "ms-render-tests-" + Guid.NewGuid().ToString("N"));
    internal static string? BrowserPath => Environment.GetEnvironmentVariable("HTML_RENDERING_BROWSER_PATH")
        ?? (OperatingSystem.IsWindows() && File.Exists("C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe")
            ? "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" : null);
    private HtmlPdfRenderer Renderer(string? dotnet = null) => new(new()
    { ExecutablePath = BrowserPath, Channel = BrowserPath is null ? HtmlBrowserChannel.MicrosoftEdge : null, TemporaryDirectory = _temporary, DotNetHostPath = dotnet ?? "dotnet" });
    public HtmlPdfRendererTests() => Directory.CreateDirectory(_temporary);
    public void Dispose() => Directory.Delete(_temporary, recursive: true);

    [BrowserFact]
    public async Task Render_RealUnicodePaginationStylesFontImageAndCallerPrefix()
    {
        var logo = Image();
        var assets = new Dictionary<string, HtmlPdfAsset>
        {
            ["logo.png"] = new("IMAGE/PNG", logo),
            ["report.css"] = new("text/css", Encoding.UTF8.GetBytes("@page{size:100mm 120mm;margin:10mm}body{font:20px Report,'Microsoft YaHei'}section{break-after:page}section:last-child{break-after:auto}.green{width:40px;height:20px;background:rgb(0,180,0)}"))
        };
        var font = Environment.GetEnvironmentVariable("HTML_RENDERING_FONT_PATH") ?? "C:/Windows/Fonts/arial.ttf";
        if (File.Exists(font))
        {
            assets["font.ttf"] = new("font/ttf", await File.ReadAllBytesAsync(font));
            assets["report.css"] = assets["report.css"] with { Content = Encoding.UTF8.GetBytes("@font-face{font-family:Report;src:url(font.ttf)}" + Encoding.UTF8.GetString(assets["report.css"].Content)) };
        }
        const string html = "<meta charset=utf-8><link rel=stylesheet href=report.css><section><h1>中文 ABC 😀</h1><img src=logo.png width=32 height=16><div class=green></div>第一页</section><section>第二页</section>";
        using var output = new MemoryStream();
        output.Write([9, 8, 7]);
        var rendering = Renderer().RenderAsync(html, output, new() { Assets = assets, PreferCssPageSize = true });
        Array.Fill(logo, (byte)0); // Input assets were snapshotted before the asynchronous worker starts.
        var result = await rendering;
        Assert.Equal(2, result.Pages);
        Assert.Equal(output.Length - 3, result.Bytes);
        Assert.Equal(new byte[] { 9, 8, 7 }, output.ToArray()[..3]);
        using var pdf = new MemoryStream(output.ToArray()[3..]);
        var processor = new PdfProcessor();
        var info = await processor.InspectAsync(pdf);
        Assert.InRange(info.Pages[0].WidthPoints, 282, 285);
        pdf.Position = 0;
        var text = await processor.ExtractTextAsync(pdf);
        Assert.Contains("中文", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("😀", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("第二页", text[1].Text, StringComparison.Ordinal);
        pdf.Position = 0;
        using var png = new MemoryStream();
        await processor.RenderPageAsync(pdf, 1, png, dpi: 72);
        using var raster = SKBitmap.Decode(png.ToArray());
        Assert.True(raster.Pixels.Count(pixel => pixel.Red > 200 && pixel.Green < 60 && pixel.Blue < 60) > 100);
        Assert.True(raster.Pixels.Count(pixel => pixel.Green > 120 && pixel.Red < 60 && pixel.Blue < 60) > 100);
        Assert.True(output.CanWrite);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserTheory]
    [InlineData("image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("image/webp", SKEncodedImageFormat.Webp)]
    public async Task Render_StaticImageFormatsDecodeAndAppearInPdf(string mime, SKEncodedImageFormat format)
    {
        using var output = new MemoryStream();
        await Renderer().RenderAsync("<img src=asset width=64 height=32>", output, new()
        { Assets = new Dictionary<string, HtmlPdfAsset> { ["asset"] = new(mime, Image(format)) } });
        output.Position = 0;
        using var png = new MemoryStream();
        await new PdfProcessor().RenderPageAsync(output, 1, png, dpi: 72);
        using var raster = SKBitmap.Decode(png.ToArray());
        Assert.True(raster.Pixels.Count(pixel => pixel.Red > 200 && pixel.Green < 60 && pixel.Blue < 60) > 100);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserFact]
    public async Task Render_AnimatedWebpAssetIsRejectedBeforeBrowserLaunch()
    {
        var animated = Convert.FromBase64String("UklGRoQAAABXRUJQVlA4WAoAAAACAAAAAQAAAQAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAAEAAAEAAGQAAAJWUDhMDwAAAC8BQAAABxD9j/4HIqL/AQBBTk1GKAAAAAAAAAAAAAEAAAEAAGQAAABWUDhMDwAAAC8BQAAABxDR//4HIqL/AQA=");
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => Renderer().RenderAsync("<img src=animated>", output, new()
        { Assets = new Dictionary<string, HtmlPdfAsset> { ["animated"] = new("image/webp", animated) } }));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserFact]
    public async Task Render_BlocksNetworkLocalFilesFramesScriptsRefreshAndBaseOverride()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stopping = new CancellationTokenSource();
        var connections = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(stopping.Token);
                    Interlocked.Increment(ref connections);
                    await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 7\r\nConnection: close\r\n\r\nNETWORK"), stopping.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        var canary = Path.Combine(_temporary, "canary.html");
        await File.WriteAllTextAsync(canary, "HOST_FILE_SECRET");
        var address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        var file = new Uri(canary).AbsoluteUri;
        var html = $$"""
            <meta charset=utf-8><base href="{{address}}/"><h1>ORIGINAL 中文</h1><img src=logo.png width=20 height=20>
            <script>document.body.innerHTML='HACK';fetch('{{address}}/script')</script>
            <link rel=stylesheet href="{{address}}/css"><style>@import url('{{address}}/import');body{background:url('{{file}}')}</style>
            <img src="{{address}}/image"><img src="{{file}}"><object data="{{file}}"></object>
            <iframe src="{{file}}" style="height:900px"></iframe><iframe src="data:text/html,FRAME_SECRET"></iframe>
            <iframe srcdoc="<script>fetch('{{address}}/nested')</script>FRAME_SECRET"></iframe>
            <div><template shadowrootmode=closed><iframe srcdoc="CLOSED_FRAME_SECRET"></iframe></template></div>
            <meta http-equiv=refresh content="0;url={{address}}/navigate">
            <link rel=preconnect href="{{address}}"><link rel=dns-prefetch href="//127.0.0.1">
            """;
        try
        {
            using var output = new MemoryStream();
            var result = await Renderer().RenderAsync(html, output, new()
            { Assets = new Dictionary<string, HtmlPdfAsset> { ["logo.png"] = new("image/png", Image()) }, WidthMillimeters = 100, HeightMillimeters = 120 });
            output.Position = 0;
            var text = string.Concat((await new PdfProcessor().ExtractTextAsync(output)).Select(page => page.Text));
            Assert.Contains("ORIGINAL 中文", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HOST_FILE_SECRET", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FRAME_SECRET", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HACK", text, StringComparison.Ordinal);
            Assert.True(result.Pages > 1); // A blocked iframe retains its CSS layout box.
            Assert.Equal(0, connections);
            Assert.Equal("HOST_FILE_SECRET", await File.ReadAllTextAsync(canary));
        }
        finally { stopping.Cancel(); await server; File.Delete(canary); }
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsTheory]
    [InlineData("html")]
    [InlineData("assetBytes")]
    [InlineData("totalAssets")]
    [InlineData("assetCount")]
    [InlineData("name")]
    [InlineData("mime")]
    [InlineData("paper")]
    [InlineData("margin")]
    [InlineData("timeout")]
    public async Task Render_RejectsInputAndPolicyBudgetsBeforeStartingWorker(string variant)
    {
        var assets = new Dictionary<string, HtmlPdfAsset> { ["a.png"] = new("image/png", [1, 2, 3]) };
        var options = variant switch
        {
            "html" => new HtmlPdfOptions { Limits = new() { MaximumHtmlBytes = 2 } },
            "assetBytes" => new() { Assets = assets, Limits = new() { MaximumAssetBytes = 2 } },
            "totalAssets" => new() { Assets = assets, Limits = new() { MaximumTotalAssetBytes = 2 } },
            "assetCount" => new() { Assets = new Dictionary<string, HtmlPdfAsset> { ["a.png"] = assets["a.png"], ["b.png"] = assets["a.png"] }, Limits = new() { MaximumAssets = 1 } },
            "name" => new() { Assets = new Dictionary<string, HtmlPdfAsset> { ["../bad"] = assets["a.png"] } },
            "mime" => new() { Assets = new Dictionary<string, HtmlPdfAsset> { ["a.js"] = new("text/javascript", [1]) } },
            "paper" => new() { WidthMillimeters = double.NaN },
            "margin" => new() { MarginMillimeters = 150 },
            _ => new() { Timeout = TimeSpan.Zero }
        };
        using var output = new MemoryStream();
        var task = Renderer().RenderAsync("中文", output, options);
        if (variant is "html" or "assetBytes" or "totalAssets" or "assetCount") await Assert.ThrowsAsync<InvalidDataException>(() => task);
        else await Assert.ThrowsAsync<ArgumentException>(() => task);
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserTheory]
    [InlineData("pixels")]
    [InlineData("imageData")]
    [InlineData("imageMime")]
    [InlineData("pages")]
    [InlineData("pdfBytes")]
    [InlineData("cssPage")]
    public async Task Render_RejectsWorkerBudgetsAndCleansBeforeAnyCallerWrites(string variant)
    {
        var options = variant switch
        {
            "pixels" => new HtmlPdfOptions { Assets = new Dictionary<string, HtmlPdfAsset> { ["a.png"] = new("image/png", Image()) }, Limits = new() { MaximumImagePixels = 1 } },
            "imageData" => new() { Assets = new Dictionary<string, HtmlPdfAsset> { ["a.png"] = new("image/png", [1, 2, 3]) } },
            "imageMime" => new() { Assets = new Dictionary<string, HtmlPdfAsset> { ["a.jpg"] = new("image/jpeg", Image()) } },
            "pages" => new() { Limits = new() { MaximumPages = 1 } },
            "pdfBytes" => new() { Limits = new() { MaximumPdfBytes = 100 } },
            _ => new() { PreferCssPageSize = true }
        };
        var html = variant == "cssPage" ? "<style>@page{size:1001mm 100mm}</style>Wide" : "<p style='break-after:page'>First</p><p>Second</p>";
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => Renderer().RenderAsync(html, output, options));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserFact]
    public async Task Render_AcceptsExactUtf8AndOutputBudgetRejectsOneByteLess()
    {
        const string html = "<meta charset=utf-8><p>边界</p>";
        using var baseline = new MemoryStream();
        await Renderer().RenderAsync(html, baseline);
        foreach (var difference in new[] { 0, -1 })
        {
            using var output = new MemoryStream();
            var task = Renderer().RenderAsync(html, output, new()
            { Limits = new() { MaximumHtmlBytes = Encoding.UTF8.GetByteCount(html), MaximumPdfBytes = (int)baseline.Length + difference } });
            if (difference == 0) Assert.Equal(baseline.Length, (await task).Bytes);
            else { await Assert.ThrowsAsync<InvalidDataException>(() => task); Assert.Empty(output.ToArray()); }
        }
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserFact]
    public async Task Render_CancellationOfActiveHeavyJobStopsOwnedWorkerBeforeReturning()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new MemoryStream();
        var task = Renderer().RenderAsync(Heavy(), output, new() { PreferCssPageSize = true }, cancellation.Token);
        await Task.Delay(1500);
        Assert.False(task.IsCompleted);
        Assert.NotEmpty(Directory.GetDirectories(_temporary));
        var clock = Stopwatch.StartNew();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(12));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserTheory]
    [InlineData(100)]
    [InlineData(400)]
    [InlineData(2000)]
    public async Task Render_TotalTimeoutIncludesActiveBrowserWorkAndCleansOwnedDirectory(int milliseconds)
    {
        using var output = new MemoryStream();
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => Renderer().RenderAsync(Heavy(), output, new() { Timeout = TimeSpan.FromMilliseconds(milliseconds), PreferCssPageSize = true }));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(12));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsFact]
    public async Task Cleanup_WaitsForSharingReleaseHonorsBudgetAndKeepsUnrelatedFiles()
    {
        var owned = Path.Combine(_temporary, "owned");
        Directory.CreateDirectory(owned);
        var untouched = Path.Combine(_temporary, "untouched.txt");
        await File.WriteAllTextAsync(untouched, "Keep");
        var file = Path.Combine(owned, "locked.dat");
        using var locked = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using (var timeout = new CancellationTokenSource(80))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, owned, timeout.Token));
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var deleting = RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, owned, budget.Token);
        await Task.Delay(80);
        Assert.False(deleting.IsCompleted);
        locked.Dispose();
        await deleting;
        Assert.False(Directory.Exists(owned));
        Assert.Equal("Keep", await File.ReadAllTextAsync(untouched));
        File.Delete(untouched);
    }

    [WindowsFact]
    public async Task Cleanup_PermissionErrorsFailImmediatelyAndOutsideRootIsRejected()
    {
        var owned = Path.Combine(_temporary, "owned");
        Directory.CreateDirectory(owned);
        var file = Path.Combine(owned, "readonly.dat");
        await File.WriteAllTextAsync(file, "Read only");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var clock = Stopwatch.StartNew();
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, owned, CancellationToken.None));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<ArgumentException>(() => RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(owned, _temporary, CancellationToken.None));
            Assert.True(File.Exists(file));
        }
        finally { File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(owned, recursive: true); }
    }

    [WindowsFact]
    public async Task Cleanup_RootPathAcceptsDescendantsAndNeverDeletesTheParentItself()
    {
        var absentChild = Path.Combine(_temporary, "absent-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(absentChild));
        await RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(Path.GetPathRoot(_temporary)!, absentChild, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, _temporary, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, _temporary + "-sibling", CancellationToken.None));
        Assert.True(Directory.Exists(_temporary));
    }

    [WindowsFact]
    public async Task Job_InheritsOwnedChildAndWaitsForItEvenAfterParentExits()
    {
        var script = Path.Combine(_temporary, "owned-parent.ps1");
        var ready = Path.Combine(_temporary, "owned-child.pid");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            [Console]::ReadLine() | Out-Null
            $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -WindowStyle Hidden -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30' -PassThru
            [IO.File]::WriteAllText($args[0], $child.Id.ToString())
            """);
        using var process = new Process
        {
            StartInfo = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true }
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, ready }) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var job = new WindowsProcessJob(process);
        Assert.Equal(1u, job.ActiveProcesses);
        await process.StandardInput.WriteLineAsync("start");
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var diagnostics = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(File.Exists(ready), await diagnostics);
            using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(ready)));
            Assert.False(child.HasExited);
            Assert.True(job.ActiveProcesses >= 1);
            job.Terminate();
            await job.WaitForEmptyAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal(0u, job.ActiveProcesses);
        }
        finally
        {
            if (job.ActiveProcesses != 0) { job.Terminate(); await job.WaitForEmptyAsync(timeout.Token); }
            if (!process.HasExited) await process.WaitForExitAsync(timeout.Token);
        }
        File.Delete(script);
        File.Delete(ready);
    }

    [BrowserFact]
    public async Task Render_TimeoutAndCleanupPermissionFailurePreserveBothCausesAndRetainedDirectory()
    {
        using var output = new MemoryStream();
        var rendering = Renderer().RenderAsync(Heavy(), output, new() { Timeout = TimeSpan.FromSeconds(2), PreferCssPageSize = true });
        var clock = Stopwatch.StartNew();
        while (Directory.GetDirectories(_temporary).Length == 0 && clock.Elapsed < TimeSpan.FromSeconds(1)) await Task.Delay(10);
        var job = Assert.Single(Directory.GetDirectories(_temporary));
        var sentinel = Path.Combine(job, "readonly-test.dat");
        await File.WriteAllTextAsync(sentinel, "Preserve cleanup error");
        File.SetAttributes(sentinel, FileAttributes.ReadOnly);
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => rendering);
            Assert.Contains(error.InnerExceptions, inner => inner is TimeoutException);
            Assert.Contains(error.InnerExceptions, inner => inner is UnauthorizedAccessException);
            Assert.Contains(job, error.Message, StringComparison.Ordinal);
            Assert.Empty(output.ToArray());
        }
        finally
        {
            File.SetAttributes(sentinel, FileAttributes.Normal);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RenderWorkerSupervisor.DeleteOwnedDirectoryAsync(_temporary, job, cleanup.Token);
        }
    }

    [Fact]
    public void Renderer_RejectsUnsupportedOperatingSystemsExplicitly()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Throws<PlatformNotSupportedException>(() => new HtmlPdfRenderer(new() { Channel = HtmlBrowserChannel.MicrosoftEdge }));
    }

    [WindowsFact]
    public async Task Render_PreCanceledAndBadDotnetStartDoNotLeakDirectories()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Renderer().RenderAsync("Text", output, cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<Win32Exception>(() => Renderer(Path.Combine(_temporary, "missing-dotnet")).RenderAsync("Text", output));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [BrowserTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Render_CallerCopyFailureOrCancellationKeepsOwnershipAndMayLeavePartialBytes(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new PartialOutput(cancel ? cancellation : null);
        var task = Renderer().RenderAsync("Output", output, cancellationToken: cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Equal(5, output.Length);
        Assert.True(output.CanWrite);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsFact]
    public async Task Worker_RejectsAccidentalInvocationAndProjectReferenceDeploysItsMetadata()
    {
        var assembly = typeof(HtmlPdfRenderer).Assembly.Location;
        Assert.True(File.Exists(Path.ChangeExtension(assembly, ".runtimeconfig.json")));
        Assert.True(File.Exists(Path.ChangeExtension(assembly, ".deps.json")));
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(assembly)!, ".playwright")));
        using var process = new Process { StartInfo = new("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add(assembly);
        process.StartInfo.ArgumentList.Add("--not-a-worker");
        process.Start();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Assert.Equal(64, process.ExitCode);
        Assert.Contains("--html-worker", await error, StringComparison.Ordinal);
        Assert.Empty(await output);
    }

    private static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(16, 8);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }
    private static string Heavy() => "<style>@page{size:100mm 120mm}section{break-after:page}</style>" + string.Concat(Enumerable.Range(0, 10_000).Select(index => $"<section><h1>Page {index}</h1>Work</section>"));
    private sealed class PartialOutput(CancellationTokenSource? cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span[..5]);
            if (cancellation is null) throw new IOException("Partial output failure");
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required.";
        else if (HtmlPdfRendererTests.BrowserPath is null) Skip = "Set HTML_RENDERING_BROWSER_PATH to an installed Chromium browser executable.";
    }
}
public sealed class BrowserTheoryAttribute : TheoryAttribute
{
    public BrowserTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required.";
        else if (HtmlPdfRendererTests.BrowserPath is null) Skip = "Set HTML_RENDERING_BROWSER_PATH to an installed Chromium browser executable.";
    }
}
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required."; }
}
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required."; }
}
