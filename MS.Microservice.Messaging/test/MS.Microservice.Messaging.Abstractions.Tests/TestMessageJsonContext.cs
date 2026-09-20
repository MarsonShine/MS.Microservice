using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Messaging.Abstractions.Tests;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MessageContractRegistryTests.Changed), TypeInfoPropertyName = "Changed")]
internal partial class TestMessageJsonContext : JsonSerializerContext;
