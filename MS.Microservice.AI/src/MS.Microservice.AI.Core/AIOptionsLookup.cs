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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        foreach (var item in options.Models.Chat)
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