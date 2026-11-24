using MS.Microservice.Documents.Internal;
using Xunit;

namespace MS.Microservice.Documents.Tests.Word;

public sealed class ExpandedPartStreamTests
{
    [Theory]
    [InlineData(3L)]
    [InlineData(long.MaxValue)]
    public async Task ActualBytesAccumulateAcrossReadsAndExactLimitAllowsEof(long limit)
    {
        using var stream = new ExpandedPartStream(new MemoryStream([1, 2, 3]), limit, default);
        var bytes = new byte[8];
        Assert.Equal(1, stream.Read(bytes, 0, 1));
        Assert.Equal(0, await stream.ReadAsync(Memory<byte>.Empty));
        Assert.Equal(2, await stream.ReadAsync(bytes.AsMemory(1)));
        Assert.Equal(0, await stream.ReadAsync(bytes.AsMemory()));
        Assert.Equal(3L, stream.BytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes[..3]);
    }

    [Fact]
    public async Task OverBudgetReadConsumesOnlyOneExtraByteAndCleanupStopsThere()
    {
        using var source = new MemoryStream(new byte[128]);
        using var stream = new ExpandedPartStream(source, 3, default);
        var buffer = new byte[64];
        Assert.Equal(2, stream.Read(buffer.AsSpan(0, 2)));
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReadAsync(buffer.AsMemory()).AsTask());
        await stream.DrainAsync();
        Assert.Equal(4L, source.Position);
    }

    [Fact]
    public async Task XmlReadsWithoutAnExplicitTokenHonorTheDocumentToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new MemoryStream(new byte[8]);
        using var stream = new ExpandedPartStream(source, 8, cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stream.ReadAsync(new byte[1].AsMemory()).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0L, source.Position);
    }
}
