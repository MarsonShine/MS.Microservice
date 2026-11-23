using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MS.Microservice.Swagger;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace MS.Microservice.Swagger.Tests;

public sealed class SwaggerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPlatformSwagger_ShouldRegisterSwaggerServicesAndConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());

        services.AddPlatformSwagger(options =>
        {
            options.DocumentTitle = "Orders API";
            options.DocumentName = "v1";
            options.Name = "Orders Docs";
        });

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ISwaggerProvider));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SwaggerGenOptions>));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SwaggerOptions>>().Value;
        options.DocumentTitle.Should().Be("Orders API");
        options.Name.Should().Be("Orders Docs");
    }

    [Fact]
    public void RepeatedRegistrationAppliesAllPlatformOptionsInOrderAndInstallsOneMvcConvention()
    {
        var services = new ServiceCollection();
        services.AddPlatformSwagger(options => options.DocumentName = "first");
        services.AddPlatformSwagger(options => options.DocumentName += "-second");
        services.AddPlatformSwagger();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<SwaggerOptions>>().Value.DocumentName.Should().Be("first-second");
        provider.GetRequiredService<IOptions<MvcOptions>>().Value.Conventions.Should().ContainSingle();
    }

    [Fact]
    public async Task PublicMiddlewareServesTheNormalizedUiAndBothPlatformAndHostDocuments()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSwaggerGen(options => options.SwaggerDoc("host", new OpenApiInfo { Title = "Host API", Version = "1" }));
        builder.Services.AddPlatformSwagger(options => options.RoutePrefix = "/docs/api/");
        builder.Services.AddPlatformSwagger(options => options.DocumentName = "platform");
        await using var app = builder.Build();
        var pipeline = new ApplicationBuilder(app.Services).UsePlatformSwagger().Build();

        (await Get("/docs/api/index.html")).Should().Contain("Swagger UI");
        (await Get("/docs/api/index.js")).Should().Contain("/docs/api/platform/swagger.json");
        (await Get("/docs/api/host/swagger.json")).Should().Contain("Host API");
        (await Get("/docs/api/platform/swagger.json")).Should().Contain(SwaggerDefaults.DefaultDocumentTitle);

        async Task<string> Get(string path)
        {
            using var scope = app.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = HttpMethods.Get;
            context.Request.PathBase = "/tenant";
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            await pipeline(context);
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MS.Microservice.Swagger.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
