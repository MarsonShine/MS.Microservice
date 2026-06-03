using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MS.Microservice.Swagger;

internal sealed class ConfigureSwaggerGenOptions(IOptions<SwaggerOptions> options) : IConfigureOptions<SwaggerGenOptions>
{
    private readonly SwaggerOptions _options = options.Value;

    public void Configure(SwaggerGenOptions swaggerGenOptions)
    {
        ArgumentNullException.ThrowIfNull(swaggerGenOptions);

        if (!_options.IsEnabled)
        {
            return;
        }

        swaggerGenOptions.SwaggerDoc(_options.DocumentName, new OpenApiInfo
        {
            Title = _options.DocumentTitle,
            Version = _options.DocumentVersion,
        });

        if (_options.EnabledSecurity)
        {
            swaggerGenOptions.ApplyBearerAuthorization();
        }

        var xmlCommentsPath = ResolveXmlCommentsPath(_options);
        if (xmlCommentsPath is not null)
        {
            swaggerGenOptions.IncludeXmlComments(xmlCommentsPath);
        }
    }

    internal static string? ResolveXmlCommentsPath(SwaggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.SwaggerXmlFile))
        {
            var explicitPath = Path.IsPathRooted(options.SwaggerXmlFile)
                ? options.SwaggerXmlFile
                : Path.Combine(AppContext.BaseDirectory, options.SwaggerXmlFile);

            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            return null;
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.xml");
        return File.Exists(defaultPath) ? defaultPath : null;
    }
}
