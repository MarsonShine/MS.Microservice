using System.Text.Json.Serialization;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuditActor))]
[JsonSerializable(typeof(CreateProfile))]
[JsonSerializable(typeof(ProfileView))]
internal sealed partial class ReferenceIdempotencyJsonContext : JsonSerializerContext;
