namespace MS.Microservice.AI.Core;

internal static class AIOptionsLookup
{
    public static bool TryGetProvider(
        AIOptions options,
        string providerName,
        out AIProviderRegistrationOptions providerOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        foreach (var entry in options.Providers)
        {
            if (string.Equals(entry.Key, providerName, StringComparison.OrdinalIgnoreCase))
            {
                providerOptions = entry.Value;
                return true;
            }
        }

        providerOptions = null!;
        return false;
    }

    public static bool TryGetChatModel(
        AIOptions options,
        string scenario,
        out KeyValuePair<string, AIChatModelOptions> entry)
    {
        return TryGetNamedEntry(options.Models.Chat, scenario, out entry);
    }

    public static bool TryGetTtsModel(
        AIOptions options,
        string scenario,
        out KeyValuePair<string, AITtsModelOptions> entry)
    {
        return TryGetNamedEntry(options.Models.Tts, scenario, out entry);
    }

    public static bool TryGetAsrModel(
        AIOptions options,
        string scenario,
        out KeyValuePair<string, AIAsrModelOptions> entry)
    {
        return TryGetNamedEntry(options.Models.Asr, scenario, out entry);
    }

    public static bool TryGetImageGenerationModel(
        AIOptions options,
        string scenario,
        out KeyValuePair<string, AIImageModelOptions> entry)
    {
        return TryGetNamedEntry(options.Models.ImageGeneration, scenario, out entry);
    }

    public static bool TryGetImageEditModel(
        AIOptions options,
        string scenario,
        out KeyValuePair<string, AIImageModelOptions> entry)
    {
        return TryGetNamedEntry(options.Models.ImageEdit, scenario, out entry);
    }

    private static bool TryGetNamedEntry<TEntry>(
        IDictionary<string, TEntry> entries,
        string scenario,
        out KeyValuePair<string, TEntry> entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        foreach (var item in entries)
        {
            if (string.Equals(item.Key, scenario, StringComparison.OrdinalIgnoreCase))
            {
                entry = item;
                return true;
            }
        }

        entry = default;
        return false;
    }
}