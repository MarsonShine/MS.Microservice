namespace MS.Microservice.Infrastructure.Telemetry;

public sealed class TelemetryResourceOptions
{
    public const string SectionName = "OpenTelemetry";

    public string ServiceName { get; set; } = "MS.Microservice.Web";
    public string ServiceNamespace { get; set; } = "MS.Microservice";
    public string ServiceVersion { get; set; } = "1.0.0";
    public string? ServiceInstanceId { get; set; }
    public string EnvironmentName { get; set; } = "Production";
    public string ActivitySourceName { get; set; } = "MS.Microservice";
}
