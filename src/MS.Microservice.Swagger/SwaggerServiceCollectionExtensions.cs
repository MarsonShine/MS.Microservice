using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Microsoft.Extensions.DependencyInjection;

public static class SwaggerServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformSwagger(
        this IServiceCollection services,
        Action<MS.Microservice.Swagger.SwaggerOptions>? setupAction = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MS.Microservice.Swagger.SwaggerOptions>();
        if (setupAction is not null)
        {
            services.Configure(setupAction);
        }

        services.AddEndpointsApiExplorer();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IConfigureOptions<SwaggerGenOptions>, MS.Microservice.Swagger.ConfigureSwaggerGenOptions>());
        services.AddSwaggerGen();

        return services;
    }
}
