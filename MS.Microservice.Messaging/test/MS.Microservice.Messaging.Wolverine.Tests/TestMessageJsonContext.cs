using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Messaging.Wolverine.Tests;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RegistrationTests.Changed), TypeInfoPropertyName = "RegisteredChanged")]
[JsonSerializable(typeof(AdapterUnitOfWorkTests.Changed), TypeInfoPropertyName = "AdapterChanged")]
internal partial class TestMessageJsonContext : JsonSerializerContext;
