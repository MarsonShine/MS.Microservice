using System.Diagnostics;
using System.Text.Json;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.Lab.Azure;

internal sealed class AzureLabScenario(IAIChatClient chat, IAITtsClient speech, IAIImageGenerationClient images,
    string output, CancellationToken cancellationToken)
{
    private const string Sol = "gpt-6.1-sol";
    private const string Terra = "gpt-6.1-terra";
    private const string ImageModel = "gpt-image-2.5-flare";
    private const string SpeechModel = "azure-speech";
    private const string SpeechInput = "你好，这是一段用于验证 Azure 语音服务的中文合成音频。";
    private const string ImagePrompt = "Create a square editorial illustration of a small white lighthouse on a rocky coast at blue hour, a single warm amber beacon above navy waves, restrained geometric composition, textured screen printing, no text or logos.";
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<int> RunAsync()
    {
        var prompt = $"This is a synthetic logistics benchmark. Reply with exactly the phrase Azure smoke test OK. " +
            $"The fictional inventory below is padding for a repeatable prompt caching measurement. Run {Path.GetFileName(output)}.\n" +
            string.Join('\n', Enumerable.Range(1, 90).Select(row =>
                $"Inventory manifest row {row:D3}: The fictional harbor warehouse stores copper coils, paper lanterns, painted flour barrels, green canvas and numbered wooden crates."));

        var sol = RunChatAsync(Sol, prompt);
        var terra = RunChatAsync(Terra, prompt);
        var audio = RunCallAsync("speech", SpeechModel, null, SynthesizeAsync);
        var image = RunCallAsync("image", ImageModel, null, GenerateImageAsync);
        await Task.WhenAll(sol, terra, audio, image);

        var chats = (await sol).Concat(await terra).ToArray();
        var calls = chats.Concat([await audio, await image]).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            ImagePrompt,
            ImageSize = "1024x1024",
            ImageQuality = "low",
            SpeechInput,
            Calls = calls,
        }, ReportJson));

        var rows = new List<string>
        {
            "deployment,run,input_tokens,cached_tokens,cache_write_tokens,output_tokens,reasoning_tokens,total_tokens,elapsed_seconds,http_status",
        };
        rows.AddRange(chats.Select(CsvRow));
        await File.WriteAllLinesAsync(Path.Combine(output, "cache-tokens.csv"), rows);

        return calls.Length == 6 && calls.All(call => call.Error is null) ? 0 : 1;
    }

    private async Task<List<CallResult>> RunChatAsync(string model, string prompt)
    {
        var request = new AIChatRequest
        {
            Provider = "Azure",
            Model = model,
            Messages = [new AIChatMessage("user", prompt)],
            MaxOutputTokens = 384,
        };
        var results = new List<CallResult>(capacity: 2);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var file = $"{model}-{attempt:00}.txt";
            var result = await RunCallAsync("chat", model, attempt, async () =>
            {
                var response = await chat.GetResponseAsync(request, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(output, file), response.Text, cancellationToken);
                return new Artifact(file, new FileInfo(Path.Combine(output, file)).Length, "text/plain",
                    response.Model, response.FinishReason, response.ProviderRequestId);
            });
            results.Add(result);
            if (result.Error is not null)
            {
                break;
            }
        }

        return results;
    }

    private async Task<Artifact> SynthesizeAsync()
    {
        var response = await speech.SynthesizeAsync(new AITtsRequest
        {
            Provider = "Azure",
            Model = SpeechModel,
            Input = SpeechInput,
            ResponseFormat = "mp3",
        }, cancellationToken);
        const string file = "azure-speech-eastus.mp3";
        await File.WriteAllBytesAsync(Path.Combine(output, file), response.Audio.Content, cancellationToken);
        return new Artifact(file, response.Audio.Content.Length, response.Audio.ContentType, response.Model, Voice: response.Voice);
    }

    private async Task<Artifact> GenerateImageAsync()
    {
        var response = await images.GenerateAsync(new AIImageGenerationRequest
        {
            Provider = "Azure",
            Model = ImageModel,
            Prompt = ImagePrompt,
            Count = 1,
            Size = "1024x1024",
            Quality = "low",
        }, cancellationToken);
        var image = response.Images.Single().Content ?? throw new InvalidDataException("Azure returned no image bytes.");
        var file = $"{ImageModel}.png";
        await File.WriteAllBytesAsync(Path.Combine(output, file), image.Content, cancellationToken);
        return new Artifact(file, image.Content.Length, image.ContentType, response.Model, RequestId: response.ProviderRequestId);
    }

    private static async Task<CallResult> RunCallAsync(string operation, string model, int? run, Func<Task<Artifact>> invoke)
    {
        var observation = new AzureHttpObservation();
        var previous = AzureUsageCaptureHandler.Current.Value;
        AzureUsageCaptureHandler.Current.Value = observation;
        var started = Stopwatch.GetTimestamp();
        Artifact? artifact = null;
        string? error = null;
        try
        {
            artifact = await invoke();
        }
        catch (AIException exception)
        {
            error = exception.ErrorCode;
            observation.RequestId ??= exception.ProviderRequestId;
            observation.Status ??= exception.StatusCode;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name;
        }
        finally
        {
            AzureUsageCaptureHandler.Current.Value = previous;
        }

        var elapsed = Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 3);
        Console.WriteLine(FormattableString.Invariant($"{operation} {model}{(run is null ? string.Empty : $" #{run}")}: {error ?? "OK"}, HTTP {observation.Status}, {elapsed:F2}s"));
        return new CallResult(operation, model, run, observation.Status, observation.RequestId ?? artifact?.RequestId,
            elapsed, artifact, observation.Usage, error);
    }

    private static string CsvRow(CallResult call)
    {
        var usage = call.Usage;
        var inputDetails = Property(usage, "input_tokens_details");
        var outputDetails = Property(usage, "output_tokens_details");
        return FormattableString.Invariant($"{call.Model},{call.Run},{Number(usage, "input_tokens")},{Number(inputDetails, "cached_tokens")},{Number(inputDetails, "cache_write_tokens")},{Number(usage, "output_tokens")},{Number(outputDetails, "reasoning_tokens")},{Number(usage, "total_tokens")},{call.ElapsedSeconds},{call.HttpStatus}");
    }

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(name, out var value) ? value : null;

    private static long? Number(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    private sealed record CallResult(string Operation, string Model, int? Run, int? HttpStatus, string? AzureRequestId,
        double ElapsedSeconds, Artifact? Artifact, JsonElement? Usage, string? Error);

    private sealed record Artifact(string File, long Bytes, string? ContentType, string ResponseModel,
        string? FinishReason = null, string? RequestId = null, string? Voice = null);
}
