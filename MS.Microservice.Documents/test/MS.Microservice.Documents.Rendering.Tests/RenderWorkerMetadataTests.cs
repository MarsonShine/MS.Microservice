using Xunit;

namespace MS.Microservice.Documents.Rendering.Tests;

public sealed class RenderWorkerMetadataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms-worker-metadata-" + Guid.NewGuid().ToString("N"));

    public RenderWorkerMetadataTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(9)]
    [InlineData(256)]
    public async Task OutputFrameIncludesOnlyBytesReadAcrossShortPipeReads(int maximum)
    {
        byte[] frame = [2, 0, 0, 0, 37, 80, 68, 70, 45];
        using var pipe = new ShortReads(frame);
        var result = await RenderWorkerSupervisor.ReadOutputAsync(pipe, maximum, "HTML", CancellationToken.None);
        Assert.Equal(frame, result.ToArray());

        pipe.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RenderWorkerSupervisor.ReadOutputAsync(pipe, frame.Length - 1, "HTML", CancellationToken.None));
    }

    private sealed class ShortReads(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }

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
