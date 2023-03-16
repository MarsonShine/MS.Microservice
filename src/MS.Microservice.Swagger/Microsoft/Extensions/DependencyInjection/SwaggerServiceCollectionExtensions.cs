using MS.Microservice.Swagger.Swagger;
using MS.Microservice.Swagger.Swagger.Autherication;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SwaggerServiceCollectionExtensions
    {
        public static void AddPlatformSwagger(this IServiceCollection service, [AllowNull] Action<SwaggerOptions> setupAction = null)
        {
            var option = new SwaggerOptions();
            setupAction?.Invoke(option);

            if (option.IsEnabled)
            {
                service.AddSwaggerGen(options =>
                {
                    options.SwaggerDoc("v1", new OpenApi.Models.OpenApiInfo
                    {
                        Title = Consts.DOC_TITLE,
                        Version = Consts.DOC_VERSION,
                    });
                    if (option.EnabledSecurity)
                    {
                        options.ApplyBearerAuthorication();
                    }

                    // comments
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, option.SwaggerXmlFile);
                    if (File.Exists(xmlPath))
                    {
                        options.IncludeXmlComments(xmlPath);
                    }

                });

            }

        }
    }
}
