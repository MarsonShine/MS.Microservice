using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using Microsoft.Playwright;
using MS.Microservice.Documents.Pdf;
using SkiaSharp;

namespace MS.Microservice.Documents.Rendering;

internal static class HtmlRenderWorker
{
    internal static async Task RunAsync()
    {
        var raw = await JsonSerializer.DeserializeAsync<HtmlWorkerRequest>(Console.OpenStandardInput()).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing HTML worker request.");
        var request = HtmlPdfRenderer.Snapshot(raw.Html, raw.Browser, raw.Options);
        foreach (var asset in request.Options.Assets.Values.Where(asset => asset.ContentType.StartsWith("image/", StringComparison.Ordinal)))
        {
            using var stream = new SKMemoryStream(asset.Content);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > request.Options.Limits.MaximumImagePixels || codec.FrameCount > 1)
                throw new InvalidDataException("An image asset is invalid, animated, or exceeds the pixel limit.");
            if (asset.ContentType != codec.EncodedFormat switch
                { SKEncodedImageFormat.Png => "image/png", SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Webp => "image/webp", _ => string.Empty })
                throw new InvalidDataException("An image asset does not match its MIME type.");
        }
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        byte[]? pdf = null;
        PdfDocumentInfo? info = null;
        Exception? failure = null;
        try
        {
            playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            browser = await playwright.Chromium.LaunchAsync(new()
            {
                ExecutablePath = request.Browser.ExecutablePath,
                Channel = request.Browser.Channel switch { HtmlBrowserChannel.MicrosoftEdge => "msedge", HtmlBrowserChannel.GoogleChrome => "chrome", _ => null },
                ChromiumSandbox = true, Headless = true, Timeout = (float)request.Options.Timeout.TotalMilliseconds,
                Args = ["--host-resolver-rules=MAP * ~NOTFOUND"]
            }).ConfigureAwait(false);
            var context = await browser.NewContextAsync(new()
            {
                JavaScriptEnabled = false, ServiceWorkers = ServiceWorkerPolicy.Block,
                AcceptDownloads = false, Offline = true, IgnoreHTTPSErrors = false
            }).ConfigureAwait(false);
            var origin = "https://render.invalid/" + Guid.NewGuid().ToString("N") + "/";
            var assets = request.Options.Assets.ToDictionary(pair => origin + Uri.EscapeDataString(pair.Key), pair => pair.Value, StringComparer.Ordinal);
            var mainRequested = false;
            await context.RouteAsync("**/*", async route =>
            {
                if (!mainRequested && route.Request.Url == origin + "index.html" && route.Request.IsNavigationRequest)
                {
                    mainRequested = true;
                    await route.FulfillAsync(new()
                    {
                        Body = request.Html, ContentType = "text/html; charset=utf-8",
                        Headers = new Dictionary<string, string>
                        {
                            ["Content-Security-Policy"] = $"sandbox allow-same-origin; default-src 'none'; script-src 'none'; style-src 'unsafe-inline' {origin}; img-src {origin}; font-src {origin}; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; connect-src 'none'; media-src 'none'",
                            ["X-Content-Type-Options"] = "nosniff"
                        }
                    }).ConfigureAwait(false);
                }
                else if (!route.Request.IsNavigationRequest && assets.TryGetValue(route.Request.Url, out var asset))
                    await route.FulfillAsync(new() { BodyBytes = asset.Content, ContentType = asset.ContentType }).ConfigureAwait(false);
                else await route.AbortAsync("blockedbyclient").ConfigureAwait(false);
            }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(origin + "index.html", new() { WaitUntil = WaitUntilState.Load, Timeout = (float)request.Options.Timeout.TotalMilliseconds }).ConfigureAwait(false);
            if (page.Url != origin + "index.html") throw new InvalidDataException("The HTML document navigated away from its isolated origin.");
            // CSP does not block srcdoc; frame topology also covers frames inside closed shadow roots.
            foreach (var frame in page.MainFrame.ChildFrames.ToArray())
                await frame.GotoAsync("about:blank", new() { WaitUntil = WaitUntilState.Load, Timeout = (float)request.Options.Timeout.TotalMilliseconds }).ConfigureAwait(false);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all(Array.from(document.images, image => image.decode().catch(() => {}))); }").ConfigureAwait(false);
            pdf = await PrintAsync(context, page, request.Options).ConfigureAwait(false);
            using var content = new MemoryStream(pdf, writable: false);
            info = await new PdfProcessor().InspectAsync(content, new()
            {
                Limits = new() { MaximumInputBytes = request.Options.Limits.MaximumPdfBytes, MaximumPages = request.Options.Limits.MaximumPages }
            }).ConfigureAwait(false);
            if (info.Pages.Any(pageInfo => pageInfo.WidthPoints > 1000 * 72 / 25.4 || pageInfo.HeightPoints > 1000 * 72 / 25.4))
                throw new InvalidDataException("A rendered PDF page exceeds 1000 millimeters.");
            await context.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (browser is not null)
            {
                try { await browser.CloseAsync().ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            try { playwright?.Dispose(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        var output = Console.OpenStandardOutput();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, info!.Pages.Count);
        await output.WriteAsync(header).ConfigureAwait(false);
        await output.WriteAsync(pdf!).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<byte[]> PrintAsync(IBrowserContext context, IPage page, HtmlPdfOptions options)
    {
        var session = await context.NewCDPSessionAsync(page).ConfigureAwait(false);
        var response = (await session.SendAsync("Page.printToPDF", new Dictionary<string, object>
        {
            ["transferMode"] = "ReturnAsStream", ["printBackground"] = options.PrintBackground,
            ["paperWidth"] = options.WidthMillimeters / 25.4, ["paperHeight"] = options.HeightMillimeters / 25.4,
            ["marginTop"] = options.MarginMillimeters / 25.4, ["marginBottom"] = options.MarginMillimeters / 25.4,
            ["marginLeft"] = options.MarginMillimeters / 25.4, ["marginRight"] = options.MarginMillimeters / 25.4,
            ["preferCSSPageSize"] = options.PreferCssPageSize, ["pageRanges"] = $"1-{options.Limits.MaximumPages + 1}"
        }).ConfigureAwait(false))!.Value;
        var handle = response.GetProperty("stream").GetString()!;
        using var pdf = new MemoryStream();
        Exception? failure = null;
        try
        {
            while (true)
            {
                var size = (int)Math.Min(81920, (long)options.Limits.MaximumPdfBytes - pdf.Length + 1);
                var read = (await session.SendAsync("IO.read", new Dictionary<string, object> { ["handle"] = handle, ["size"] = size }).ConfigureAwait(false))!.Value;
                var data = read.GetProperty("data").GetString()!;
                if (data.Length > ((long)size + 2) / 3 * 4) throw new InvalidDataException("Unexpected PDF stream chunk size.");
                var chunk = read.TryGetProperty("base64Encoded", out var encoded) && encoded.GetBoolean()
                    ? Convert.FromBase64String(data) : Encoding.UTF8.GetBytes(data);
                if (chunk.Length > options.Limits.MaximumPdfBytes - pdf.Length) throw new InvalidDataException("Rendered PDF exceeds the byte limit.");
                var required = (int)pdf.Length + chunk.Length;
                if (required > pdf.Capacity) pdf.Capacity = (int)Math.Min(options.Limits.MaximumPdfBytes, Math.Max(required, Math.Max(256L, pdf.Capacity * 2L)));
                pdf.Write(chunk);
                if (read.GetProperty("eof").GetBoolean()) return pdf.ToArray();
                if (chunk.Length == 0) throw new InvalidDataException("The PDF stream made no progress.");
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { await session.SendAsync("IO.close", new Dictionary<string, object> { ["handle"] = handle }).ConfigureAwait(false); }
            catch (Exception error) when (failure is not null) { throw new AggregateException(failure, error); }
        }
    }
}
