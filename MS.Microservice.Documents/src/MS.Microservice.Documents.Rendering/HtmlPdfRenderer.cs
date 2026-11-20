using System.Text;

namespace MS.Microservice.Documents.Rendering;

/// <summary>Renders static HTML on Windows using an isolated Chromium worker and bounded supplied assets.</summary>
public sealed class HtmlPdfRenderer
{
    private readonly HtmlBrowserOptions _browser;
    private static readonly HashSet<string> AssetTypes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "text/css", "font/woff", "font/woff2", "font/ttf", "font/otf" };

    public HtmlPdfRenderer(HtmlBrowserOptions browser)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("HTML rendering currently requires Windows process-job supervision.");
        ArgumentNullException.ThrowIfNull(browser);
        if ((browser.ExecutablePath is null) == (browser.Channel is null)
            || browser.Channel is { } channel && !Enum.IsDefined(channel)
            || string.IsNullOrWhiteSpace(browser.DotNetHostPath) || !Path.IsPathFullyQualified(browser.TemporaryDirectory))
            throw new ArgumentException("Configure one installed browser path or channel, a dotnet host, and an absolute temporary directory.", nameof(browser));
        if (browser.ExecutablePath is { } path && (!Path.IsPathFullyQualified(path) || !File.Exists(path)))
            throw new ArgumentException("The browser executable must be an existing absolute file path.", nameof(browser));
        _browser = browser with { TemporaryDirectory = Path.GetFullPath(browser.TemporaryDirectory) };
    }

    /// <summary>Validates a completed PDF before copying at the destination's current position; the caller stream stays open.</summary>
    public async Task<HtmlPdfResult> RenderAsync(string html, Stream destination, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Output must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        var request = Snapshot(html, _browser, options ?? new());
        var (bytes, pages) = await RenderWorkerSupervisor.RunAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new(pages, bytes.Length);
    }

    internal static HtmlWorkerRequest Snapshot(string html, HtmlBrowserOptions browser, HtmlPdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options.Assets);
        ArgumentNullException.ThrowIfNull(options.Limits);
        var limits = options.Limits;
        if (limits.MaximumHtmlBytes < 1 || limits.MaximumAssets < 1 || limits.MaximumAssetBytes < 1
            || limits.MaximumTotalAssetBytes < 1 || limits.MaximumImagePixels < 1 || limits.MaximumPdfBytes is < 1 or > int.MaxValue - 4
            || limits.MaximumPages is < 1 or > 10_000 || options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(10)
            || !double.IsFinite(options.WidthMillimeters) || options.WidthMillimeters is < 10 or > 1000
            || !double.IsFinite(options.HeightMillimeters) || options.HeightMillimeters is < 10 or > 1000
            || !double.IsFinite(options.MarginMillimeters) || options.MarginMillimeters < 0
            || options.MarginMillimeters * 2 >= Math.Min(options.WidthMillimeters, options.HeightMillimeters))
            throw new ArgumentException("HTML rendering limits, timeout, paper dimensions or margins are invalid.", nameof(options));
        if (html.Length > limits.MaximumHtmlBytes || new UTF8Encoding(false, true).GetByteCount(html) > limits.MaximumHtmlBytes)
            throw new InvalidDataException("HTML exceeds the UTF-8 input limit.");
        if (options.Assets.Count > limits.MaximumAssets) throw new InvalidDataException("Too many HTML assets.");
        var assets = new Dictionary<string, HtmlPdfAsset>(StringComparer.Ordinal);
        long total = 0;
        foreach (var (name, asset) in options.Assets)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name is "." or ".." or "index.html"
                || name.IndexOfAny(['/', '\\', ':', '?', '#', '%']) >= 0 || asset is null || asset.Content is null || !AssetTypes.Contains(asset.ContentType))
                throw new ArgumentException("Assets require unique single file names and a supported image, CSS or font MIME type.", nameof(options));
            if (asset.Content.Length > limits.MaximumAssetBytes || asset.Content.Length > limits.MaximumTotalAssetBytes - total)
                throw new InvalidDataException("HTML assets exceed the configured byte limits.");
            total += asset.Content.Length;
            _ = new UTF8Encoding(false, true).GetByteCount(name);
            assets.Add(name, asset with { ContentType = asset.ContentType.ToLowerInvariant(), Content = asset.Content.ToArray() });
        }
        return new(html, browser, options with { Assets = assets });
    }
}
