using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Core.NativeAot.Smoke;

var failed = 0;
await Run("native-runtime", () =>
{
    Require(!RuntimeFeature.IsDynamicCodeSupported && !RuntimeFeature.IsDynamicCodeCompiled,
        "Run the published Native AOT executable; a managed test run is not native validation.");
    Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection must be disabled.");
    return Task.CompletedTask;
});
await Run("consumer-json-contract", () =>
{
    var contracts = new JsonTypeRegistry(SmokeJson.Default.SmokePayload);
    var value = new SmokePayload(42, "Core 原生消费");
    var json = JsonSerializer.Serialize(value, contracts.Get<SmokePayload>());
    var restored = JsonSerializer.Deserialize(json, contracts.Get<SmokePayload>());
    Require(restored == value, "Consumer-generated JSON metadata did not preserve the payload.");
    return Task.CompletedTask;
});
foreach (var (name, scenario) in SerializationScenarios.All)
    await Run(name, scenario);
foreach (var (name, scenario) in FoundationScenarios.All)
    await Run(name, scenario);
Console.WriteLine($"Native AOT smoke: {failed} failed.");
return failed == 0 ? 0 : 1;

async Task Run(string name, Func<Task> scenario)
{
    try
    {
        await scenario();
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
