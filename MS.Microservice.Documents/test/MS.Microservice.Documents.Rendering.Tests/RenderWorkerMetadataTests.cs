using Xunit;

namespace MS.Microservice.Documents.Rendering.Tests;

public sealed class RenderWorkerMetadataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms-worker-metadata-" + Guid.NewGuid().ToString("N"));

    public RenderWorkerMetadataTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Metadata_UsesConsumerPairWithSpacesAndMultipleDotsBeforeFrameworkMetadata()
    {
        var consumer = Pair("Consumer.App with spaces");
        var framework = Pair("Microsoft.AspNetCore.App");
        var result = RenderWorkerSupervisor.ResolveHostMetadata(consumer.Dependencies + ";" + framework.Dependencies);
        Assert.Equal(consumer, result);
        Assert.NotEqual(consumer.Dependencies + ".runtimeconfig.json", result.RuntimeConfig);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Consumer.deps.json")]
    [InlineData("relative/Consumer.deps.json")]
    public void Metadata_MissingOrRelativeHostContextIsExplicitlyUnsupported(string? dependencies)
        => Assert.Throws<NotSupportedException>(() => RenderWorkerSupervisor.ResolveHostMetadata(dependencies));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Metadata_MissingConsumerFileDoesNotFallBackToAFrameworkPair(bool removeDependencies)
    {
        var consumer = Pair("Consumer");
        var framework = Pair("Microsoft.AspNetCore.App");
        File.Delete(removeDependencies ? consumer.Dependencies : consumer.RuntimeConfig);
        Assert.Throws<NotSupportedException>(() => RenderWorkerSupervisor.ResolveHostMetadata(consumer.Dependencies + ";" + framework.Dependencies));
    }

    [Fact]
    public void Metadata_CurrentTestConsumerExposesItsOwnPairRatherThanTheTesthostEntryAssembly()
    {
        var result = RenderWorkerSupervisor.ResolveHostMetadata(AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, typeof(RenderWorkerMetadataTests).Assembly.GetName().Name + ".deps.json"), result.Dependencies);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, typeof(RenderWorkerMetadataTests).Assembly.GetName().Name + ".runtimeconfig.json"), result.RuntimeConfig);
    }

    private (string RuntimeConfig, string Dependencies) Pair(string name)
    {
        var runtime = Path.Combine(_directory, name + ".runtimeconfig.json");
        var dependencies = Path.Combine(_directory, name + ".deps.json");
        File.WriteAllText(runtime, "{}");
        File.WriteAllText(dependencies, "{}");
        return (runtime, dependencies);
    }
}
