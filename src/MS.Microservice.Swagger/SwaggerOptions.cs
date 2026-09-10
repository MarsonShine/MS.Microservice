namespace MS.Microservice.Swagger;

/// <summary>
/// Controls Swagger document generation and Swagger UI behavior.
/// </summary>
public sealed class SwaggerOptions
{
    public const string SectionName = "SwaggerOptions";
    public bool IsEnabled { get; set; } = true;

    public bool EnabledSecurity { get; set; }

    public string? SwaggerXmlFile { get; set; }

    public string? RoutePrefix { get; set; } = SwaggerDefaults.DefaultRoutePrefix;

    public bool IsAuth { get; set; }

    public string? Name { get; set; } = SwaggerDefaults.DefaultDisplayName;

    public string DocumentName { get; set; } = SwaggerDefaults.DefaultDocumentName;

    public string DocumentTitle { get; set; } = SwaggerDefaults.DefaultDocumentTitle;

    public string DocumentVersion { get; set; } = SwaggerDefaults.DefaultDocumentVersion;

    public string? CustomJavascriptPath { get; set; }
}
