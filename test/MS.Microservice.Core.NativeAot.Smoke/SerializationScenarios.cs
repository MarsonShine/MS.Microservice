using System.Collections;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Core.Net.Http;
using MS.Microservice.Core.Serialization;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal static class SerializationScenarios
{
    private static readonly ConsumerJson Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("http-json-graph", JsonGraph), ("json-contract-boundaries", MetadataBoundaries),
        ("http-polymorphism", Polymorphism), ("http-null-body", NullBody),
        ("http-failures-cancellation", HttpFailures), ("query-explicit-map", QueryMapping),
        ("http-extension-metadata", HttpExtension), ("cache-roundtrip-bom", CacheRoundTrip),
        ("cache-factory-empty-hit", CacheFactory), ("cache-failures-cancellation", CacheFailures)
    ];

    private static HttpClient Http(RecordingHandler handler) => new(handler) { BaseAddress = new("https://example.test") };
    private static LogHttpClient Client(HttpClient http, params System.Text.Json.Serialization.Metadata.JsonTypeInfo[] contracts)
        => new(NullLogger<LogHttpClient>.Instance, http, new(contracts));
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Throws<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task JsonGraph()
    {
        var handler = new RecordingHandler();
        using var http = Http(handler);
        var client = Client(http, Json.Batch);
        foreach (var name in new string?[] { null, "", "中文 <tag>&" })
        {
            var value = new Item(7, name, new("A-01"));
            var result = await client.PostAsync<Envelope<List<Item>>>("/echo", new Envelope<List<Item>>([value]),
                new Dictionary<string, string> { ["X-Request"] = "one" });
            Check(result?.Data.Single() == value, "Closed generic, collection or converter roundtrip changed.");
            Check(handler.MediaType == "application/json" && handler.Charset == "utf-8", "POST content type changed.");
            Check(handler.HasRequestHeader && !http.DefaultRequestHeaders.Contains("X-Request"), "Request headers leaked into defaults.");
            using var document = JsonDocument.Parse(handler.Body);
            Check(document.RootElement.GetProperty("data")[0].GetProperty("code").GetString() == "A-01", "Custom converter was not used.");
            if (name?.Contains('中') == true)
                Check(handler.Body.Contains("中文") && handler.Body.Contains("\\u003C"), "Supplied encoder was ignored.");
        }
        var empty = await client.PostAsync<Envelope<List<Item>>>("/echo", new Envelope<List<Item>>([]));
        Check(empty?.Data.Count == 0 && !handler.HasRequestHeader, "Empty collection or header isolation changed.");
    }

    private static async Task MetadataBoundaries()
    {
        Check(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection must stay disabled.");
        await Throws<ArgumentException>(() => { _ = new JsonTypeRegistry(Json.Item, Json.Item); return Task.CompletedTask; });
        var handler = new RecordingHandler();
        using var http = Http(handler);
        var restricted = Client(http, EnvelopeOnlyJson.Default.ObjectEnvelope, Json.Item);
        await Throws<NotSupportedException>(() => restricted.PostAsync<Item>("/echo", new Unknown(1)).AsTask());
        await Throws<NotSupportedException>(() => restricted.GetAsync<Unknown>("/echo", null).AsTask());
        await Throws<NotSupportedException>(() => restricted.PostAsync<Item>("/echo", new ObjectEnvelope(new Unknown(1))).AsTask());
        // Root registration does not extend the resolver attached to a nested object property.
        await Throws<NotSupportedException>(() => restricted.PostAsync<Item>("/echo", new ObjectEnvelope(new Item(1, "known root", new("X")))).AsTask());
        Check(handler.Calls == 0, "Missing metadata must fail before transport.");
        var complete = Client(http, Json.ObjectEnvelope);
        var result = await complete.PostAsync<ObjectEnvelope>("/echo", new ObjectEnvelope(new Item(1, "known nested", new("X"))));
        Check(result?.Data is JsonElement element && element.GetProperty("id").GetInt32() == 1, "Known object value failed; object deserializes as JsonElement.");
        Check(handler.Calls == 1, "The valid nested contract should reach transport once.");
    }

    private static async Task Polymorphism()
    {
        var handler = new RecordingHandler();
        using var http = Http(handler);
        var expected = new AnimalEnvelope(new Dog("Milo", 3));
        var result = await Client(http, Json.AnimalEnvelope).PostAsync<AnimalEnvelope>("/echo", expected);
        Check(result == expected && result.Animal is Dog, "Declared polymorphism lost the derived type.");
        Check(handler.Body.Contains("\"kind\":\"dog\""), "Polymorphic discriminator is missing.");
    }

    private static async Task NullBody()
    {
        var handler = new RecordingHandler();
        using var http = Http(handler);
        var result = await Client(http, Json.Item).PostAsync<Item>("/echo", null);
        Check(result is null && handler.Body == "null" && handler.Calls == 1, "Null request/response behavior changed.");
    }

    private static async Task HttpFailures()
    {
        var handler = new RecordingHandler();
        using var http = Http(handler);
        var client = Client(http, Json.Item);
        handler.Respond = _ => Task.FromResult(RecordingHandler.Json("{}", HttpStatusCode.BadGateway));
        await Throws<HttpRequestException>(() => client.GetAsync<Item>("/json", null).AsTask());
        handler.Respond = _ => Task.FromResult(RecordingHandler.Json("{broken"));
        await Throws<JsonException>(() => client.GetAsync<Item>("/json", null).AsTask());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var before = handler.Calls;
        await Throws<OperationCanceledException>(() => client.GetAsync<Item>("/json", null, cancelled.Token).AsTask());
        Check(handler.Calls == before, "Pre-cancelled request reached transport.");
        using var duringSend = new CancellationTokenSource();
        handler.Respond = token => { duringSend.Cancel(); return Task.FromCanceled<HttpResponseMessage>(token); };
        await Throws<OperationCanceledException>(() => client.GetAsync<Item>("/json", null, duringSend.Token).AsTask());
        Check(handler.Calls == before + 1, "Cancellation during transport was not exercised.");
    }

    private static async Task QueryMapping()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("fr-FR");
        try
        {
            var handler = new RecordingHandler();
            using var http = Http(handler);
            var client = Client(http, Json.Item);
            var date = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var map = new QueryParameterMap<Query>(("label", q => q.Label), ("amount", q => q.Amount),
                ("at", q => q.At), ("items", q => q.Items), ("absent", q => q.Absent), ("empty", q => q.Empty));
            await client.GetAsync<Item, Query>("/query?kept=1#tail", new("中文 &", 1.25m, date, [1, null, 2]), map);
            Check(handler.Uri!.Query == "?kept=1&label=%E4%B8%AD%E6%96%87%20%26&amount=1.25&at=2024-01-02T03%3A04%3A05.0000000%2B00%3A00&items=1&items=2&empty=",
                "Mapped query escaping, nulls, date or invariant number formatting changed.");
            Check(handler.Uri.Fragment == "#tail", "Existing fragment was lost.");
            await client.GetAsync<Item>("/query", new Hashtable { ["items"] = new[] { 3, 4 } });
            Check(handler.Uri!.Query == "?items=3&items=4", "Dictionary collection expansion changed.");
            await client.GetAsync<Item, Query>("/query?kept=1#tail", null, map);
            Check(handler.Uri!.Query == "?kept=1" && handler.Uri.Fragment == "#tail", "Null query changed the URI.");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private sealed record Query(string Label, decimal Amount, DateTimeOffset At, object?[] Items)
    {
        public string? Absent => null;
        public string Empty => "";
        public string Unselected => throw new InvalidOperationException("Unmapped properties must not be read.");
    }

    private static async Task HttpExtension()
    {
        var handler = new RecordingHandler { Respond = _ => Task.FromResult(RecordingHandler.Json("{\"ID\":9,\"NAME\":\"case\",\"CODE\":\"C\"}")) };
        using var http = Http(handler);
        var value = await MS.WebHttpClient.HttpClientExtensions.GetAsync<Item>(http, "/json", null, Json.Item);
        Check(value == new Item(9, "case", new("C")), "HTTP extension ignored supplied metadata or case rules.");
        handler.Respond = _ => Task.FromResult(RecordingHandler.Json("{broken"));
        Check(await MS.WebHttpClient.HttpClientExtensions.GetAsync<Item>(http, "/json", null, Json.Item) is null,
            "The older HTTP extension's default-on-invalid-JSON behavior changed.");
    }

    private static async Task CacheRoundTrip()
    {
        var cache = new RecordingCache();
        var value = new Item(0, "中文", new("C"));
        using var cancellation = new CancellationTokenSource();
        var absolute = TimeSpan.FromMinutes(5);
        var sliding = TimeSpan.FromMinutes(1);
        await cache.SetAsync("item", value, Json.Item, absolute, sliding, cancellation.Token);
        Check(await cache.GetAsync("item", Json.Item, cancellation.Token) == value, "Cache roundtrip changed.");
        Check(cache.Options!.AbsoluteExpirationRelativeToNow == absolute && cache.Options.SlidingExpiration == sliding,
            "Cache expiration options were not forwarded.");
        Check(cache.ReadToken == cancellation.Token && cache.WriteToken == cancellation.Token, "Cache tokens were not forwarded.");
        var bytes = cache.Get("item")!;
        cache.Set("bom", [.. Encoding.UTF8.Preamble, .. bytes], new());
        Check(await cache.GetAsync("bom", Json.Item) == value, "UTF-8 BOM was not accepted.");
        Check(await cache.GetAsync("missing", Json.Item) is null, "A missing key should return default.");
    }

    private static async Task CacheFactory()
    {
        var cache = new RecordingCache();
        var calls = 0;
        Task<string?> Empty() { calls++; return Task.FromResult<string?>(""); }
        Check(!(await cache.TryGetValueAsync("empty", Empty, Json.String)).Success && cache.Writes == 0,
            "An empty factory result should not be cached.");
        Task<string?> Value() { calls++; return Task.FromResult<string?>("value"); }
        Check(await cache.TryGetValueAsync("key", Value, Json.String) == (true, "value"), "Factory miss failed.");
        Check(await cache.TryGetValueAsync("key", Value, Json.String) == (true, "value"), "Cache hit failed.");
        Check(calls == 2 && cache.Writes == 1, "Cache hit reran the factory or wrote again.");
    }

    private static async Task CacheFailures()
    {
        var cache = new RecordingCache();
        cache.Set("invalid", "{broken"u8.ToArray(), new());
        await Throws<JsonException>(() => cache.GetAsync("invalid", Json.Item));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Throws<OperationCanceledException>(() => cache.GetAsync("invalid", Json.Item, cancelled.Token));
        Check(cache.ReadToken == cancelled.Token, "Cache read did not receive cancellation.");
        await Throws<ArgumentNullException>(() => cache.SetAsync<Item>("null", null!, Json.Item));
    }
}
