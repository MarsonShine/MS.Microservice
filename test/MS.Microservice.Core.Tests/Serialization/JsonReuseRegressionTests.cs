using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.Json.Serialization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MS.WebHttpClient;
using Xunit.Abstractions;

namespace MS.Microservice.Core.Tests.Serialization;

public sealed partial class JsonReuseRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheOperations_AvoidRepeatedConfigurationAllocations(bool write)
    {
        var value = new Payload("中文", 7, State.Active, new("nested"));
        var cache = new RecordingCache { Data = "{\"Name\":\"中文\",\"Count\":7}"u8.ToArray() };
        Func<Task> operation = write
            ? () => cache.SetAsync("item", value, CacheJson.Payload, null, null)
            : () => cache.GetAsync("item", CacheJson.Payload);
        for (int i = 0; i < 32; i++) Assert.True(operation().IsCompletedSuccessfully);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++) Assert.True(operation().IsCompletedSuccessfully);
        long bytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - before) / 64;

        output.WriteLine($"Cache {(write ? "write" : "read")}: {bytesPerOperation} bytes/op after warmup.");
        // A tiny payload needs far less than 8 KiB; rebuilding the encoder/config costs over 20 KiB.
        Assert.True(bytesPerOperation < 8192, $"Allocated {bytesPerOperation} bytes/op.");
    }

    [Fact]
    public async Task HttpReads_AvoidRepeatedConfigurationAllocations()
    {
        using var client = Client("{\"name\":\"中文\",\"count\":7}");
        for (int i = 0; i < 32; i++) await client.GetAsync<Payload>("/item", body: null!, HttpJson.Payload);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            var pending = client.GetAsync<Payload>("/item", body: null!, HttpJson.Payload);
            Assert.True(pending.IsCompletedSuccessfully); // The fake transport contains only in-memory data.
            var value = await pending;
            Assert.Equal(7, value.Count);
        }
        long bytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - before) / 64;

        output.WriteLine($"HTTP read: {bytesPerOperation} bytes/op after warmup.");
        Assert.True(bytesPerOperation < 16384, $"Allocated {bytesPerOperation} bytes/op.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("中文 <tag>& \"quoted\"")]
    public async Task CacheRoundTrip_PreservesNamesNullsEnumsNestedValuesAndExpiration(string? name)
    {
        var cache = new RecordingCache();
        var original = new Payload(name, 0, State.Active, new("child"));
        using var cancellation = new CancellationTokenSource();
        var absolute = TimeSpan.FromMinutes(10);
        var sliding = TimeSpan.FromMinutes(1);

        await cache.SetAsync("item", original, CacheJson.Payload, absolute, sliding, cancellation.Token);
        var decoded = await cache.GetAsync("item", CacheJson.Payload, cancellation.Token);

        Assert.Equal(original, decoded);
        using var document = JsonDocument.Parse(cache.Data!);
        Assert.True(document.RootElement.TryGetProperty("Name", out var property));
        Assert.Equal(name, property.GetString());
        Assert.False(document.RootElement.TryGetProperty("name", out _));
        Assert.Equal(1, document.RootElement.GetProperty("State").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("Count").GetInt32());
        if (name?.Contains('中') == true)
        {
            var json = Encoding.UTF8.GetString(cache.Data!);
            Assert.Contains("中文", json);
            Assert.Contains("\\u003C", json);
            Assert.Contains("\\u0026", json);
        }
        Assert.Equal(absolute, cache.Options!.AbsoluteExpirationRelativeToNow);
        Assert.Equal(sliding, cache.Options.SlidingExpiration);
        Assert.Equal(cancellation.Token, cache.ReadToken);
        Assert.Equal(cancellation.Token, cache.WriteToken);
    }

    [Fact]
    public async Task CacheAndHttp_KeepDifferentPropertyNameMatchingRules()
    {
        const string json = "{\"nAmE\":\"value\",\"cOuNt\":3}";
        var cache = new RecordingCache { Data = Encoding.UTF8.GetBytes(json) };
        using var client = Client(json);

        var cached = await cache.GetAsync("item", CacheJson.Payload);
        var http = await client.GetAsync<Payload>("/item", body: null!, HttpJson.Payload);

        Assert.NotNull(cached);
        Assert.Null(cached.Name);
        Assert.Equal(0, cached.Count);
        Assert.Equal("value", http.Name);
        Assert.Equal(3, http.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"Count\":\"3\"}")]
    public async Task CacheAndHttp_PreserveTheirDifferentInvalidJsonPolicies(string json)
    {
        var cache = new RecordingCache { Data = Encoding.UTF8.GetBytes(json) };
        using var client = Client(json);

        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync("item", CacheJson.Payload));
        Assert.Null(await client.GetAsync<Payload>("/item", body: null!, HttpJson.Payload));
    }

    [Fact]
    public async Task CacheHitAndMiss_PreserveFactoryAndWriteBehavior()
    {
        var cache = new RecordingCache();
        int calls = 0;
        var original = new Payload("factory", 2, State.Active, null);
        Task<Payload> Factory() { calls++; return Task.FromResult(original); }

        Assert.Equal(original, await cache.GetAsync("item", Factory, CacheJson.Payload));
        Assert.Equal(original, await cache.GetAsync("item", Factory, CacheJson.Payload));
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Writes);
        cache.Data = "null"u8.ToArray();
        Assert.Null(await cache.GetAsync("item", CacheJson.Payload));
        cache.Data = null;
        Assert.Null(await cache.GetAsync("item", CacheJson.Payload));
    }

    [Fact]
    public async Task CacheRead_ObservesCancellationAfterBackendCompletes()
    {
        using var cancellation = new CancellationTokenSource();
        var cache = new RecordingCache { Data = "{}"u8.ToArray(), AfterRead = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync("item", CacheJson.Payload, cancellation.Token));
        Assert.Equal(cancellation.Token, cache.ReadToken);
    }

    [Theory]
    [InlineData("{\"Name\":\"BOM\"}")]
    [InlineData("  {\"Name\":\"BOM\"} \r\n")]
    public async Task CacheRead_PreservesLeadingUtf8BomSupport(string json)
    {
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(json)];
        var cache = new RecordingCache { Data = bytes };

        Assert.Equal("BOM", (await cache.GetAsync("item", CacheJson.Payload))!.Name);
        Assert.Same(bytes, cache.Data);
        Assert.True(cache.Data.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Theory]
    [InlineData("\uFEFF")]
    [InlineData(" \uFEFF{}")]
    [InlineData("\uFEFF\uFEFF{}")]
    public async Task CacheRead_RejectsEmptyOrMisplacedBomInput(string json)
    {
        var cache = new RecordingCache { Data = Encoding.UTF8.GetBytes(json) };
        await Assert.ThrowsAsync<JsonException>(() => cache.GetAsync("item", CacheJson.Payload));
    }

    [Fact]
    public async Task ConcurrentCalls_ReuseConfigurationWithoutMixingValues()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            var original = new Payload($"值{index}", index, State.Active, new(index.ToString()));
            var cache = new RecordingCache();
            await cache.SetAsync("item", original, CacheJson.Payload, null, null);
            Assert.Equal(original, await cache.GetAsync("item", CacheJson.Payload));
        })));
    }

    private static readonly ReuseTestJson CacheJson = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });
    private static readonly ReuseTestJson HttpJson = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    [JsonSerializable(typeof(Payload))]
    private partial class ReuseTestJson : JsonSerializerContext;

    public enum State { Inactive, Active }
    public sealed record Child(string Value);
    public sealed record Payload(string? Name, int Count, State State, Child? Details);

    private static HttpClient Client(string json) => new(new JsonHandler(json)) { BaseAddress = new Uri("https://example.test") };

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class RecordingCache : IDistributedCache
    {
        public byte[]? Data { get; set; }
        public Action? AfterRead { get; init; }
        public DistributedCacheEntryOptions? Options { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public CancellationToken WriteToken { get; private set; }
        public int Writes { get; private set; }
        public byte[]? Get(string key) => Data;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ReadToken = token;
            AfterRead?.Invoke();
            return Task.FromResult(Data);
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Data = value;
            Options = options;
            Writes++;
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            WriteToken = token;
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Data = null;
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    }
}
