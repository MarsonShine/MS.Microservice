using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Swagger;
using ISwaggerProvider = Swashbuckle.AspNetCore.Swagger.ISwaggerProvider;

foreach (int count in new[] { 1, 16 })
{
    MeasuredOperation.Run($"Swagger / register {count} time(s)", 512, () =>
    {
        var services = new ServiceCollection();
        for (int i = 0; i < count; i++)
        {
            services.AddPlatformSwagger(static options => options.DocumentName += "x");
        }
        return services;
    }, services =>
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SwaggerOptions>>().Value.DocumentName == "v1" + new string('x', count)
            && services.Any(descriptor => descriptor.ServiceType == typeof(ISwaggerProvider));
    });
}

foreach (string prefix in new[] { "swagger", "/tools/private/api-docs/" })
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.Services.AddPlatformSwagger(options => options.RoutePrefix = prefix);
    using var host = builder.Build();
    MeasuredOperation.Run($"Swagger / configure middleware {prefix}", 512,
        () => new ApplicationBuilder(host.Services).UsePlatformSwagger(),
        app =>
        {
            var pipeline = app.Build();
            string route = "/" + prefix.Trim('/');
            return Serves(pipeline, host.Services, route + "/v1/swagger.json", "application/json")
                && Serves(pipeline, host.Services, route + "/index.html", "text/html");
        });
}

static bool Serves(RequestDelegate pipeline, IServiceProvider services, string path, string mediaType)
{
    using var body = new MemoryStream();
    var context = new DefaultHttpContext { RequestServices = services };
    context.Request.Method = "GET";
    context.Request.Path = path;
    context.Response.Body = body;
    pipeline(context).GetAwaiter().GetResult();
    return context.Response.StatusCode == StatusCodes.Status200OK && body.Length > 0
        && context.Response.ContentType?.StartsWith(mediaType, StringComparison.Ordinal) == true;
}
