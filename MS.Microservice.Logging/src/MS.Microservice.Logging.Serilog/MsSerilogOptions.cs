using Microsoft.Extensions.Logging;
using Serilog;

namespace MS.Microservice.Logging.Serilog;

/// <summary>
/// Configures the MS.Microservice Serilog provider wrapper.
/// </summary>
public sealed class MsSerilogOptions
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool ClearProviders { get; set; } = true;

    public bool ReadFromConfiguration { get; set; } = true;

    public bool UseConsoleSink { get; set; } = true;

    public Action<IServiceProvider, LoggerConfiguration>? ConfigureLogger { get; set; }
}