using Microsoft.Extensions.Logging;

namespace MS.Microservice.Logging.NLog;

/// <summary>
/// Configures the MS.Microservice NLog provider wrapper.
/// </summary>
public sealed class MsNLogOptions
{
    public string ConfigurationFilePath { get; set; } = "nlog.config";

    public LogLevel? MinimumLevel { get; set; } = LogLevel.Information;

    public bool ClearProviders { get; set; } = true;

    public bool UseFallbackConfigurationWhenFileMissing { get; set; } = true;

    public string NetworkAddress { get; set; } = "tcp://127.0.0.1:5000";
}