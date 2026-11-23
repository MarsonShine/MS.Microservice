using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Microsoft.Extensions.DependencyInjection;

public static class SwaggerServiceCollectionExtensions
{
    private static readonly ServiceDescriptor PlatformConfigurator =
        ServiceDescriptor.Transient<IConfigureOptions<SwaggerGenOptions>, MS.Microservice.Swagger.ConfigureSwaggerGenOptions>();

    extension(IServiceCollection services)
    {
        public IServiceCollection AddPlatformSwagger(Action<MS.Microservice.Swagger.SwaggerOptions>? setupAction = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (setupAction is not null)
            {
                services.Configure(setupAction);
            }

            if (!services.Contains(PlatformConfigurator))
            {
                services.AddOptions();
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen();
                services.Add(PlatformConfigurator);
            }

            return services;
        }
    }
}
