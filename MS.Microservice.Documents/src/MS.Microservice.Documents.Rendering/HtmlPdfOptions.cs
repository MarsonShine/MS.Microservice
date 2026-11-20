namespace MS.Microservice.Documents.Rendering;

public enum HtmlBrowserChannel { MicrosoftEdge, GoogleChrome }

/// <summary>Trusted host configuration; paths and browser selection must not come from submitted HTML.</summary>
public sealed record HtmlBrowserOptions
{
    public string? ExecutablePath { get; init; }
    public HtmlBrowserChannel? Channel { get; init; }
    public string DotNetHostPath { get; init; } = "dotnet";
    public string TemporaryDirectory { get; init; } = Path.GetTempPath();
}

public sealed record HtmlPdfAsset(string ContentType, byte[] Content);

public sealed record HtmlPdfLimits
{
    public int MaximumHtmlBytes { get; init; } = 4 * 1024 * 1024;
    public int MaximumAssets { get; init; } = 64;
    public int MaximumAssetBytes { get; init; } = 4 * 1024 * 1024;
    public long MaximumTotalAssetBytes { get; init; } = 32L * 1024 * 1024;
    public long MaximumImagePixels { get; init; } = 20_000_000;
    public int MaximumPdfBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumPages { get; init; } = 100;
}

public sealed record HtmlPdfOptions
{
    public IReadOnlyDictionary<string, HtmlPdfAsset> Assets { get; init; } = new Dictionary<string, HtmlPdfAsset>();
    public double WidthMillimeters { get; init; } = 210;
    public double HeightMillimeters { get; init; } = 297;
    public double MarginMillimeters { get; init; } = 10;
    public bool PrintBackground { get; init; } = true;
    public bool PreferCssPageSize { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public HtmlPdfLimits Limits { get; init; } = new();
}

public sealed record HtmlPdfResult(int Pages, int Bytes);

internal sealed record HtmlWorkerRequest(string Html, HtmlBrowserOptions Browser, HtmlPdfOptions Options);
