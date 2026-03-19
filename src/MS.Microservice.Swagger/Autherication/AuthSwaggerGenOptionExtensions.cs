using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MS.Microservice.Swagger.Swagger.Autherication
{
    public static partial class AuthSwaggerGenOptionExtensions
    {
        extension(SwaggerGenOptions options)
        {
            // TODO:待参数化
            public void ApplyBearerAuthorication()
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
}
