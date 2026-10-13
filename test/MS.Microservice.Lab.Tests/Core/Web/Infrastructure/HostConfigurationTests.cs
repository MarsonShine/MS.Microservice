using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using MS.Microservice.Lab.Hosting;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class HostConfigurationTests
{
    [Theory]
    [InlineData("Lab")]
    [InlineData("Development")]
    public void DeployedEnvironmentUsesTheLabXmlComments(string environment)
    {
        var builder = PlatformWebHost.CreateBuilder(["--environment", environment]);
        try
        {
            Assert.Equal("MS.Microservice.Lab.xml", builder.Configuration["SwaggerOptions:SwaggerXmlFile"]);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "MS.Microservice.Lab.xml")));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Lab")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void EntryPoint_LoadsDeployedSettings_AndPreservesCommandLineOverrides(string environment)
    {
        var builder = PlatformWebHost.CreateBuilder([
            "--environment", environment,
            "--ConnectionStrings:ActivationConnection", "Host=explicit-override;Database=test"
        ]);
        try
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                Path.TrimEndingDirectorySeparator(builder.Environment.ContentRootPath));
            var settings = builder.Configuration.Sources.OfType<JsonConfigurationSource>()
                .Single(source => source.Path == "appsettings.json");
            Assert.True(settings.FileProvider!.GetFileInfo(settings.Path!).Exists);
            Assert.Equal("Host=explicit-override;Database=test",
                builder.Configuration.GetConnectionString("ActivationConnection"));
            Assert.Equal(environment, builder.Environment.EnvironmentName);
        }
        finally
        {
            ((IDisposable)builder.Configuration).Dispose();
        }
    }
}
