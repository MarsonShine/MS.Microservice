using System.Text.Json;

namespace MS.Microservice.Lab.Tests.Tools;

public sealed class ToolDemoRuntimeContextTests
{
    [Fact]
    public void TestHostExposesItsConsumerDependencyAndFrameworkMetadata()
    {
        var deps = Assert.IsType<string>(AppContext.GetData("APP_CONTEXT_DEPS_FILES"));
        var primary = deps.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.EndsWith(".deps.json", primary, StringComparison.Ordinal);
        var runtime = primary[..^".deps.json".Length] + ".runtimeconfig.json";
        Assert.True(File.Exists(primary), primary);
        Assert.True(File.Exists(runtime), runtime);
        using var json = JsonDocument.Parse(File.ReadAllText(runtime));
        Assert.Contains(json.RootElement.GetProperty("runtimeOptions").GetProperty("frameworks").EnumerateArray(),
            framework => framework.GetProperty("name").GetString() == "Microsoft.AspNetCore.App");
        Console.WriteLine("Consumer deps: " + primary);
        Console.WriteLine("Consumer runtimeconfig: " + runtime);
        Console.WriteLine("Managed entry: " + System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name);
    }
}
