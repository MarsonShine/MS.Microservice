using System.Text.Json.Serialization;
using MS.Microservice.AspNetCore.Encryption;

namespace MS.Microservice.Lab.Infrastructure.Encryption;

public sealed record EncryptedEchoRequest(string Message) : IApiEncrypt;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EncryptedEchoRequest))]
public sealed partial class LabApiEncryptionJsonContext : JsonSerializerContext;
