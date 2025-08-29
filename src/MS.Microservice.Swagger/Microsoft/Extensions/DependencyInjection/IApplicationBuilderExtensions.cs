using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using MS.Microservice.Swagger.Swagger;

namespace MS.Microservice.Swagger.Microsoft.Extensions.DependencyInjection
{
    public static class IApplicationBuilderExtensions
    {
        public static void UsePlatformSwagger(this IApplicationBuilder app)
        {
            var configuration = app.ApplicationServices.GetService<IConfiguration>()!;
            var swaggerOption = configuration.GetSection("SwaggerOptions").Get<SwaggerOptions>();
            if (swaggerOption == null) return;

            if (swaggerOption.IsEnabled)
            {
                app.UseSwagger(c => {
                    c.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                    // 如果有自定义路由前缀，配置模板
                    if (!string.IsNullOrWhiteSpace(swaggerOption.RoutePrefix))
                    {
                        c.RouteTemplate = $"{swaggerOption.RoutePrefix}/{{documentName}}/swagger.json";
                    }
                });

                app.UseSwaggerUI(c =>
                {
                    c.OAuthClientId("activationswaggerui");
                    c.OAuthAppName("ActivationSystem Swagger UI");

                    // 构建正确的 JSON 端点路径
                    var routePrefix = string.IsNullOrWhiteSpace(swaggerOption.RoutePrefix) ? "swagger" : swaggerOption.RoutePrefix;
                    var jsonEndpoint = $"/{routePrefix}/v1/swagger.json";

                    c.InjectJavascript("/swagger-custom.js");
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", swaggerOption.Name);
                    c.RoutePrefix = swaggerOption.RoutePrefix;

                    // 如果启用了认证，添加一些 UI 配置
                    if (swaggerOption.IsAuth)
                    {
                        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
                        c.DefaultModelsExpandDepth(-1);
                    }
                });
            }
        }
    }
}
