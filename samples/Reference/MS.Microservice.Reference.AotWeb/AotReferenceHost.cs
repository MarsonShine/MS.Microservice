using MS.Microservice.AspNetCore;
using MS.Microservice.Logging.AspNetCore;

namespace MS.Microservice.Reference.AotWeb;

public static class AotReferenceHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders().AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });
        return builder;
    }

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AotWebJsonContext.Default));
        builder.Services.AddPlatformHttp(builder.Configuration);
        builder.Services.AddMsRequestLogging();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
    }

    public static void MapApplication(WebApplication app)
    {
        app.UsePlatformHttp();
        app.UseMsRequestLogging();
        app.MapGet("/health/live", () => TypedResults.Ok(new LivenessResponse("healthy"))).AllowAnonymous();
    }
}
