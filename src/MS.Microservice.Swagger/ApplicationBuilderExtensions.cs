using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UsePlatformSwagger(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<IOptions<MS.Microservice.Swagger.SwaggerOptions>>().Value;
        if (!options.IsEnabled)
        {
            return app;
        }

        app.UseSwagger(swaggerOptions =>
        {
            swaggerOptions.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            swaggerOptions.RouteTemplate = MS.Microservice.Swagger.SwaggerPathBuilder.BuildRouteTemplate(options.RoutePrefix);
        });

        app.UseSwaggerUI(swaggerUiOptions =>
        {
            swaggerUiOptions.OAuthClientId("msmicroservice-swagger-ui");
            swaggerUiOptions.OAuthAppName("MS.Microservice Swagger UI");
            swaggerUiOptions.SwaggerEndpoint(
                MS.Microservice.Swagger.SwaggerPathBuilder.BuildJsonEndpoint(options.RoutePrefix, options.DocumentName),
                options.Name ?? options.DocumentTitle);
            swaggerUiOptions.RoutePrefix = MS.Microservice.Swagger.SwaggerPathBuilder.NormalizeUiRoutePrefix(options.RoutePrefix);

            if (!string.IsNullOrWhiteSpace(options.CustomJavascriptPath))
            {
                swaggerUiOptions.InjectJavascript(options.CustomJavascriptPath);
            }

            if (options.IsAuth)
            {
                swaggerUiOptions.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
                swaggerUiOptions.DefaultModelsExpandDepth(-1);
            }
        });

        return app;
    }
}
