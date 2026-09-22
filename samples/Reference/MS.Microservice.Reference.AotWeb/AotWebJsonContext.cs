using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MS.Microservice.Reference.AotWeb;

internal sealed record LivenessResponse(string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LivenessResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class AotWebJsonContext : JsonSerializerContext;
