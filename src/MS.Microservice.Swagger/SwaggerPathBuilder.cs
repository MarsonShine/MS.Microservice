namespace MS.Microservice.Swagger;

internal static class SwaggerPathBuilder
{
    public static string BuildRouteTemplate(string normalizedPrefix)
    {
        return normalizedPrefix.Length == 0
            ? "{documentName}/swagger.json"
            : $"{normalizedPrefix}/{{documentName}}/swagger.json";
    }

    public static string BuildJsonEndpoint(string normalizedPrefix, string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        return normalizedPrefix.Length == 0
            ? $"/{documentName}/swagger.json"
            : $"/{normalizedPrefix}/{documentName}/swagger.json";
    }

    public static string NormalizeRoutePrefix(string? routePrefix)
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
