using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MS.Microservice.Swagger;

namespace Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UsePlatformSwagger()
        {
            ArgumentNullException.ThrowIfNull(app);

            var options = app.ApplicationServices.GetRequiredService<IOptions<SwaggerOptions>>().Value;
            if (!options.IsEnabled)
            {
                return app;
            }

            app.UseSwagger(swaggerOptions =>
            {
                swaggerOptions.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                var prefix = SwaggerPathBuilder.NormalizeRoutePrefix(options.RoutePrefix);
                swaggerOptions.RouteTemplate = SwaggerPathBuilder.BuildRouteTemplate(prefix);
            });

            app.UseSwaggerUI(swaggerUiOptions =>
            {
                var prefix = SwaggerPathBuilder.NormalizeRoutePrefix(options.RoutePrefix);
                swaggerUiOptions.OAuthClientId("msmicroservice-swagger-ui");
                swaggerUiOptions.OAuthAppName("MS.Microservice Swagger UI");
                swaggerUiOptions.SwaggerEndpoint(
                    SwaggerPathBuilder.BuildJsonEndpoint(prefix, options.DocumentName),
                    options.Name ?? options.DocumentTitle);
                swaggerUiOptions.RoutePrefix = prefix;

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
}
