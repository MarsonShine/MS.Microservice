using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UnitOfWorkTests.Changed), TypeInfoPropertyName = "Changed")]
internal partial class TestMessageJsonContext : JsonSerializerContext;
