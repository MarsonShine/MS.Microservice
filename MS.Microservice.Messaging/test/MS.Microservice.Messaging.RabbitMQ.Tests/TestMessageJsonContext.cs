using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS.Microservice.Messaging.RabbitMQ.Tests;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ConsumerLifetimeTests.Changed), TypeInfoPropertyName = "Changed")]
internal partial class TestMessageJsonContext : JsonSerializerContext;
