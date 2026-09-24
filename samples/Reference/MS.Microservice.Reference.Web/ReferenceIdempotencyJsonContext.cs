using System.Text.Json.Serialization;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AuditActor))]
[JsonSerializable(typeof(CreateProfile))]
internal sealed partial class ReferenceIdempotencyJsonContext : JsonSerializerContext;
