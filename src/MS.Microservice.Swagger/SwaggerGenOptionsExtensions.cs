using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MS.Microservice.Swagger;

internal static class SwaggerGenOptionsExtensions
{
    public static void ApplyBearerAuthorization(this SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string name = "bearer";
        options.AddSecurityDefinition(name, new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = name,
            BearerFormat = "JWT",
            Description = "JWT Authorization header using the Bearer scheme.",
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(name, document)] = new List<string>(),
        });
    }
}
