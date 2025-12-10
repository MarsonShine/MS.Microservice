using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MS.Microservice.Swagger.Swagger.Autherication
{
    public static class AuthSwaggerGenOptionExtensions
    {
        // TODO:待参数化
        public static void ApplyBearerAuthorication(this SwaggerGenOptions options)
        {
            const string name = "bearer";
            options.AddSecurityDefinition(name, new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = name,
                BearerFormat = "JWT",
                Description = "JWT Authorization header using the Bearer scheme."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(name, document)] = []
            });
        }
    }
}
