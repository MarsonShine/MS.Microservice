using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.Core.Serialization;

var failed = 0;
Run("native-runtime", () =>
{
    Require(!RuntimeFeature.IsDynamicCodeSupported && !RuntimeFeature.IsDynamicCodeCompiled,
        "Run the published Native AOT executable; a managed test run is not native validation.");
    Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection must be disabled.");
});
Run("consumer-json-contract", () =>
{
    var contracts = new JsonTypeRegistry(SmokeJson.Default.SmokePayload);
    var value = new SmokePayload(42, "Core 原生消费");
    var json = JsonSerializer.Serialize(value, contracts.Get<SmokePayload>());
    var restored = JsonSerializer.Deserialize(json, contracts.Get<SmokePayload>());
    Require(restored == value, "Consumer-generated JSON metadata did not preserve the payload.");
});
Console.WriteLine($"Native AOT smoke: {failed} failed.");
return failed == 0 ? 0 : 1;

void Run(string name, Action scenario)
{
    try
    {
        scenario();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record SmokePayload(int Id, string Name);

[JsonSerializable(typeof(SmokePayload))]
internal partial class SmokeJson : JsonSerializerContext;
