using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
