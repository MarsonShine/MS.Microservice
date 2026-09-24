using System.Text.Json.Serialization;

namespace MS.Microservice.AspNetCore.Encryption;

public sealed record SafeDataWrapper(string? Key, string? Info);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SafeDataWrapper))]
internal sealed partial class ApiEncryptedBodyJsonContext : JsonSerializerContext;
