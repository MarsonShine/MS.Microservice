using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Core.Serialization;

namespace MS.Microservice.Aot.Tests;

public sealed partial class MetadataBoundaryTests
{
    [Fact]
    public void ReflectionIsDisabledAndUnknownContractsFailExplicitly()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var registry = new JsonTypeRegistry(Contracts.Envelope);
        Assert.Same(Contracts.Envelope, registry.Get<Envelope>());
        Assert.Throws<NotSupportedException>(() => registry.Get<Unknown>());
        Assert.Throws<ArgumentException>(() => new JsonTypeRegistry(Contracts.Envelope, Contracts.Envelope));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnregisteredRootOrNestedValueFailsBeforeTransport(bool nested)
    {
        var handler = new EchoHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new LogHttpClient(NullLogger<LogHttpClient>.Instance, http, new(Contracts.Envelope));
        object value = nested ? new Envelope("parent", new Unknown(1)) : new Unknown(1);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.PostAsync<Envelope>("/echo", value).AsTask());
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("中文 <tag>&")]
    public async Task PostUsesSuppliedEncodingAndCaseRules(string? name)
    {
        var handler = new EchoHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new LogHttpClient(NullLogger<LogHttpClient>.Instance, http, new(Contracts.Envelope));
        var result = await client.PostAsync<Envelope>("/echo", new Envelope(name, null));
        Assert.Equal(name, result!.Name);
        if (name is not null)
        {
            Assert.Contains("中文", handler.Body);
            Assert.Contains("\\u003C", handler.Body);
        }
        Assert.Contains("\"Data\":null", handler.Body);
    }

    [Fact]
    public async Task NullPostBodyAndMissingResponseMetadataHaveDefinedBehavior()
    {
        var handler = new EchoHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new LogHttpClient(NullLogger<LogHttpClient>.Instance, http, new(Contracts.Envelope));
        Assert.Null(await client.PostAsync<Envelope>("/echo", null));
        Assert.Equal("null", handler.Body);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetAsync<Unknown>("/echo", null).AsTask());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CacheTryGetPreservesEmptyFactoryAndHitSemantics()
    {
        var cache = new MemoryCache();
        int calls = 0;
        Task<string?> Empty() { calls++; return Task.FromResult<string?>(""); }
        Assert.False((await cache.TryGetValueAsync("key", Empty, Contracts.String)).Success);
        Assert.Null(cache.Data);
        Task<string?> Value() { calls++; return Task.FromResult<string?>("value"); }
        Assert.Equal((true, "value"), await cache.TryGetValueAsync("key", Value, Contracts.String));
        Assert.Equal((true, "value"), await cache.TryGetValueAsync("key", Value, Contracts.String));
        Assert.Equal(2, calls);
    }

    private static readonly BoundaryJson Contracts = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    public sealed record Envelope(string? Name, object? Data);
    private sealed record Unknown(int Value);

    [JsonSerializable(typeof(Envelope))]
    [JsonSerializable(typeof(string))]
    private partial class BoundaryJson : JsonSerializerContext;

    private sealed class EchoHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(Body.Replace("\"Name\"", "\"nAmE\""), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class MemoryCache : IDistributedCache
    {
        public byte[]? Data { get; private set; }
        public byte[]? Get(string key) => Data;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Data);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Data = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
        public void Remove(string key) => Data = null;
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
