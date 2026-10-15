using System.Net;
using System.Text;
using MS.WebHttpClient;
using Xunit.Abstractions;

namespace MS.Microservice.Core.Tests.Serialization;

public sealed class JsonReuseRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task HttpReadsAvoidRepeatedConfigurationAllocations()
    {
        using var client = Client("{\"name\":\"中文\",\"count\":7}");
        for (var index = 0; index < 32; index++) await client.GetAsync<Payload>("/item", body: null!);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 64; index++)
        {
            var pending = client.GetAsync<Payload>("/item", body: null!);
            Assert.True(pending.IsCompletedSuccessfully);
            Assert.Equal(7, (await pending).Count);
        }
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 64;
        output.WriteLine($"HTTP read: {bytes} bytes/op after warmup.");
        Assert.True(bytes < 16384, $"Allocated {bytes} bytes/op.");
    }
    [Fact]
    public async Task HttpPropertyNamesRemainCaseInsensitive()
    {
        using var client = Client("{\"nAmE\":\"value\",\"cOuNt\":3}");
        var value = await client.GetAsync<Payload>("/item", body: null!);
        Assert.Equal("value", value.Name); Assert.Equal(3, value.Count);
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"Count\":\"3\"}")]
    public async Task HttpInvalidJsonStillReturnsNull(string json)
    {
        using var client = Client(json);
        Assert.Null(await client.GetAsync<Payload>("/item", body: null!));
    }
    public sealed record Payload(string? Name, int Count);
    private static HttpClient Client(string json) => new(new JsonHandler(json)) { BaseAddress = new Uri("https://example.test") };
    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
