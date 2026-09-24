using System.Text.Json.Serialization;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuditActor))]
internal sealed partial class ReferenceIdempotencyJsonContext : JsonSerializerContext;
