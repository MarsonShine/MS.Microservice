using Microsoft.Extensions.Options;

namespace MS.Microservice.AI.Core;

internal static class AIProviderRegistrationValidation
{
    public static ValidateOptionsResult ValidateProvider(
        AIOptions options,
        string providerName,
        string fallbackBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackBaseAddress);

        var isReferenced = string.Equals(options.DefaultProvider, providerName, StringComparison.OrdinalIgnoreCase)
            || options.Models.Chat.Values.Any(model => string.Equals(model.Provider, providerName, StringComparison.OrdinalIgnoreCase))
            || AIOptionsLookup.TryGetProvider(options, providerName, out _);

        if (!isReferenced)
        {
            return ValidateOptionsResult.Success;
        }

        if (!AIOptionsLookup.TryGetProvider(options, providerName, out var providerOptions))
        {
            return ValidateOptionsResult.Fail($"AI:Providers:{providerName} must be configured because provider '{providerName}' is registered or referenced.");
        }

        var failures = new List<string>();

        if (!providerOptions.Enabled)
        {
            failures.Add($"AI:Providers:{providerName}:Enabled must be true when the provider is in use.");
        }

        if (string.IsNullOrWhiteSpace(providerOptions.ApiKey))
        {
            failures.Add($"AI:Providers:{providerName}:ApiKey is required.");
        }

        var baseAddress = string.IsNullOrWhiteSpace(providerOptions.BaseAddress)
            ? fallbackBaseAddress
            : providerOptions.BaseAddress;

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out _))
        {
            failures.Add($"AI:Providers:{providerName}:BaseAddress must be a valid absolute URI.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}