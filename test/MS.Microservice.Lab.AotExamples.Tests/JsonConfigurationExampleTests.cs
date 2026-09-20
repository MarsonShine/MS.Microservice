using System.Text;
using Xunit;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.JsonConfigurationExample;
using StaticExample = MS.Microservice.Lab.AotExamples.Static.JsonConfigurationExample;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class JsonConfigurationExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("中文 <tag>& \"quoted\"")]
    public async Task CacheProfiles_ProduceIdenticalBytesAndRoundTrips(string? text)
    {
        var expected = new Payload(text, 7);
        var legacyBytes = LegacyExample.WriteCache(expected);
        var staticBytes = StaticExample.WriteCache(expected);

        Assert.Equal(legacyBytes, staticBytes);
        Assert.Equal(expected, await LegacyExample.ReadCacheAsync<Payload>(staticBytes));
        Assert.Equal(expected, StaticExample.ReadCache<Payload>(legacyBytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothVersions_KeepCacheCaseSensitivityAndHttpCaseInsensitivity(bool bom)
    {
        byte[] json = "{\"nAmE\":\"value\",\"cOuNt\":3}"u8.ToArray();
        if (bom) json = [.. Encoding.UTF8.Preamble, .. json];
        var legacyCache = await LegacyExample.ReadCacheAsync<Payload>(json);
        var staticCache = StaticExample.ReadCache<Payload>(json);
        using var legacyStream = new MemoryStream(json);
        using var staticStream = new MemoryStream(json);

        var legacyHttp = await LegacyExample.ReadHttpAsync<Payload>(legacyStream);
        var staticHttp = await StaticExample.ReadHttpAsync<Payload>(staticStream);

        Assert.Equal(new Payload(null, 0), legacyCache);
        Assert.Equal(legacyCache, staticCache);
        Assert.Equal(new Payload("value", 3), legacyHttp);
        Assert.Equal(legacyHttp, staticHttp);
    }

    [Fact]
    public async Task BothVersions_ObserveCancellationBeforeReading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var json = "{}"u8.ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LegacyExample.ReadCacheAsync<Payload>(json, cancellation.Token).AsTask());
        Assert.ThrowsAny<OperationCanceledException>(() => StaticExample.ReadCache<Payload>(json, cancellation.Token));
        using var stream = new MemoryStream(json);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StaticExample.ReadHttpAsync<Payload>(stream, cancellation.Token).AsTask());
    }

    public sealed record Payload(string? Name, int Count);
}
