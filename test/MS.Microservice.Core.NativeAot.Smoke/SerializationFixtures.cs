using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal sealed record Item(int Id, string? Name, ItemCode Code);
internal sealed record Envelope<T>(T Data);
internal sealed record ObjectEnvelope(object? Data);
internal sealed record Unknown(int Id);
[JsonConverter(typeof(ItemCodeConverter))]
internal readonly record struct ItemCode(string Value);
internal sealed class ItemCodeConverter : JsonConverter<ItemCode>
{
    public override ItemCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("Code cannot be null."));
    public override void Write(Utf8JsonWriter writer, ItemCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Dog), "dog")]
internal abstract record Animal(string Name);
internal sealed record Dog(string Name, int Age) : Animal(Name);
internal sealed record AnimalEnvelope(Animal Animal);

[JsonSerializable(typeof(Item))]
[JsonSerializable(typeof(Envelope<List<Item>>), TypeInfoPropertyName = "Batch")]
[JsonSerializable(typeof(ObjectEnvelope))]
[JsonSerializable(typeof(AnimalEnvelope))]
[JsonSerializable(typeof(string))]
internal partial class ConsumerJson : JsonSerializerContext;

// The registry can contain Item, while this envelope's resolver still does not know Item.
[JsonSerializable(typeof(ObjectEnvelope))]
internal partial class EnvelopeOnlyJson : JsonSerializerContext;

internal sealed class RecordingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string Body { get; private set; } = "";
    public Uri? Uri { get; private set; }
    public string? MediaType { get; private set; }
    public string? Charset { get; private set; }
    public bool HasRequestHeader { get; private set; }
    public Func<CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Uri = request.RequestUri;
        HasRequestHeader = request.Headers.Contains("X-Request");
        MediaType = request.Content?.Headers.ContentType?.MediaType;
        Charset = request.Content?.Headers.ContentType?.CharSet;
        Body = request.Content is null ? "null" : await request.Content.ReadAsStringAsync(cancellationToken);
        return Respond is null ? Json(Body) : await Respond(cancellationToken);
    }
}

internal sealed class RecordingCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> values = [];
    public int Writes { get; private set; }
    public DistributedCacheEntryOptions? Options { get; private set; }
    public CancellationToken ReadToken { get; private set; }
    public CancellationToken WriteToken { get; private set; }
    public byte[]? Get(string key) => values.GetValueOrDefault(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ReadToken = token;
        // Deliberately do not throw here: Core must also check cancellation before parsing.
        return Task.FromResult(Get(key));
    }
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    { values[key] = value; Options = options; Writes++; }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    { WriteToken = token; Set(key, value, options); return Task.CompletedTask; }
    public void Remove(string key) => values.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
}
