using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using MS.Microservice.Lab.Hosting;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class HostConfigurationTests
{
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
