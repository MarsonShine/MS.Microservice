namespace MS.Microservice.Swagger;

internal static class SwaggerPathBuilder
{
    public static string BuildRouteTemplate(string? routePrefix)
    {
        var prefix = NormalizeRoutePrefix(routePrefix);
        return string.IsNullOrEmpty(prefix)
            ? "{documentName}/swagger.json"
            : $"{prefix}/{{documentName}}/swagger.json";
    }

    public static string BuildJsonEndpoint(string? routePrefix, string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        var prefix = NormalizeRoutePrefix(routePrefix);
        return string.IsNullOrEmpty(prefix)
            ? $"/{documentName}/swagger.json"
            : $"/{prefix}/{documentName}/swagger.json";
    }

    public static string NormalizeUiRoutePrefix(string? routePrefix) => NormalizeRoutePrefix(routePrefix);

    private static string NormalizeRoutePrefix(string? routePrefix)
    {
        if (routePrefix is null)
        {
            return SwaggerDefaults.DefaultRoutePrefix;
        }

        if (routePrefix.Length == 0)
        {
            return string.Empty;
        }

        var trimmed = routePrefix.Trim('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? SwaggerDefaults.DefaultRoutePrefix
            : trimmed;
    }
}
