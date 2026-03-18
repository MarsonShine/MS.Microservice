using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using MS.Microservice.Swagger.Swagger;
using MS.Microservice.Swagger.Swagger.Autherication;
using System;
using System.IO;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class SwaggerServiceCollectionExtensions
    {
        extension(IServiceCollection service)
        {
            public void AddPlatformSwagger(Action<SwaggerOptions>? setupAction = null)
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
}
