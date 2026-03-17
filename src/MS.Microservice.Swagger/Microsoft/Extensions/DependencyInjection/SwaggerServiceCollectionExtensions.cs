using Microsoft.OpenApi;
using MS.Microservice.Swagger.Swagger;
using MS.Microservice.Swagger.Swagger.Autherication;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SwaggerServiceCollectionExtensions
    {
        public static void AddPlatformSwagger(this IServiceCollection service, [AllowNull] Action<SwaggerOptions> setupAction = null)
        {
            SwaggerOptions option = new();
            setupAction?.Invoke(option);

            if (option.IsEnabled)
            {
                service.AddSwaggerGen(options =>
                {
                    options.SwaggerDoc("v1", new OpenApiInfo
                    {
                        Title = Consts.DOC_TITLE,
                        Version = Consts.DOC_VERSION,
                    });
                    if (option.EnabledSecurity)
                    {
                        options.ApplyBearerAuthorication();
                    }

                    options.IncludeXmlComments(Assembly.GetExecutingAssembly());
                    // or options.IncludeXmlComments(typeof(MyController).Assembly));

                });

            }

        }
    }
}
