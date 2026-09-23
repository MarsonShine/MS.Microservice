using System.Diagnostics;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

const int iterations = 200_000;
const int rounds = 5;
var request = new AIChatRequest
{
    Messages = [new AIChatMessage("user", "hello")],
    Scenario = "target",
};

foreach (var count in new[] { 1, 16, 128 })
{
    var options = new AIOptions();
    for (var index = 0; index < count; index++)
    {
        options.Providers.Add($"Provider{index}", new AIProviderRegistrationOptions());
        options.Models.Chat.Add($"Scenario{index}", new AIChatModelOptions
        {
            Provider = $"Provider{index}",
            Model = "model",
        });
    }

    options.Models.Chat.Add("Target", new AIChatModelOptions
    {
        Provider = $"Provider{count - 1}",
        Model = "model",
    });
    var resolver = new DefaultAIModelResolver(Options.Create(options));
    Measure($"chat, {count} providers/scenarios, last match", () => resolver.ResolveChatModel(request));
}

foreach (var count in new[] { 1, 16, 128 })
{
    var options = new AIOptions();
    options.Providers.Add(DeepSeekProviderDefaults.ProviderName, new AIProviderRegistrationOptions
    {
        ApiKey = "test-key",
    });
    for (var index = 0; index < count; index++)
    {
        options.Models.Tts.Add($"Tts{index}", new AITtsModelOptions { Provider = "OpenAI" });
        options.Models.Asr.Add($"Asr{index}", new AIAsrModelOptions { Provider = "OpenAI" });
        options.Models.ImageGeneration.Add($"Generate{index}", new AIImageModelOptions { Provider = "OpenAI" });
        options.Models.ImageEdit.Add($"Edit{index}", new AIImageModelOptions { Provider = "OpenAI" });
    }

    var validator = new DeepSeekOptionsValidator();
    Measure($"DeepSeek validation, {count} models/capability", () => validator.Validate(null, options), 20_000);
}

void Measure<T>(string name, Func<T> resolve, int calls = iterations)
{
    for (var i = 0; i < calls / 10; i++) _ = resolve();

    var times = new double[rounds];
    var allocations = new double[rounds];
    for (var round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++) GC.KeepAlive(resolve());
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / calls;
        allocations[round] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / calls;
    }

    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[rounds / 2]:F1} ns/op, {allocations[rounds / 2]:F1} B/op (median of {rounds} x {calls:N0})");
}
