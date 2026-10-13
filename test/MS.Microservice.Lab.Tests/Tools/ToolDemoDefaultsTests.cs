using Microsoft.Extensions.Configuration;
using MS.Microservice.Lab.Application.Tools;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class ToolDemoDefaultsTests
{
    [Fact]
    public void LibreOfficeUsesInstalledPathUnlessAnExplicitOverrideIsProvided()
    {
        using var host = new ToolDemoTestHost();
        var directory = Path.Combine(host.Root, "office with spaces");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "soffice.com");
        File.WriteAllText(executable, "discovery fixture; never executed");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory);
            foreach (var value in new string?[] { null, "", "   " })
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ToolDemos:LibreOfficeExecutablePath"] = value }).Build();
                Assert.Equal(executable, ToolDemoDefaults.LibreOfficeExecutable(configuration));
            }
            var overridden = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ToolDemos:LibreOfficeExecutablePath"] = Path.Combine(host.Root, "missing.com") }).Build();
            Assert.Throws<ToolDependencyException>(() => ToolDemoDefaults.LibreOfficeExecutable(overridden));
            overridden["ToolDemos:LibreOfficeExecutablePath"] = executable;
            Assert.Equal(executable, ToolDemoDefaults.LibreOfficeExecutable(overridden));
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
    }
}
