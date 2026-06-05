using Microsoft.Extensions.Options;

namespace MS.Microservice.AI.Core;

public sealed class AIOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.DefaultProvider)
            && !AIOptionsLookup.TryGetProvider(options, options.DefaultProvider, out _))
        {
            failures.Add($"AI:DefaultProvider '{options.DefaultProvider}' does not exist under AI:Providers.");
        }

        foreach (var provider in options.Providers)
        {
            if (provider.Value.TimeoutSeconds <= 0)
            {
                failures.Add($"AI:Providers:{provider.Key}:TimeoutSeconds must be greater than 0.");
            }

            if (provider.Value.MaxRetryAttempts < 0)
            {
                failures.Add($"AI:Providers:{provider.Key}:MaxRetryAttempts cannot be negative.");
            }

            if (provider.Value.ConcurrencyLimit <= 0)
            {
                failures.Add($"AI:Providers:{provider.Key}:ConcurrencyLimit must be greater than 0.");
            }
        }

        foreach (var model in options.Models.Chat)
        {
            ValidateModel(model.Key, model.Value.Provider, model.Value.Model, model.Value.TimeoutSeconds, model.Value.MaxRetryAttempts, options, failures, "Chat");
        }

        foreach (var model in options.Models.Tts)
        {
            ValidateModel(model.Key, model.Value.Provider, model.Value.Model, model.Value.TimeoutSeconds, model.Value.MaxRetryAttempts, options, failures, "Tts");
        }

        foreach (var model in options.Models.Asr)
        {
            ValidateModel(model.Key, model.Value.Provider, model.Value.Model, model.Value.TimeoutSeconds, model.Value.MaxRetryAttempts, options, failures, "Asr");
        }

        foreach (var model in options.Models.ImageGeneration)
        {
            ValidateModel(model.Key, model.Value.Provider, model.Value.Model, model.Value.TimeoutSeconds, model.Value.MaxRetryAttempts, options, failures, "ImageGeneration");
            if (model.Value.Count is <= 0)
            {
                failures.Add($"AI:Models:ImageGeneration:{model.Key}:Count must be greater than 0 when provided.");
            }
        }

        foreach (var model in options.Models.ImageEdit)
        {
            ValidateModel(model.Key, model.Value.Provider, model.Value.Model, model.Value.TimeoutSeconds, model.Value.MaxRetryAttempts, options, failures, "ImageEdit");
            if (model.Value.Count is <= 0)
            {
                failures.Add($"AI:Models:ImageEdit:{model.Key}:Count must be greater than 0 when provided.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateModel(
        string key,
        string provider,
        string model,
        int? timeoutSeconds,
        int? maxRetryAttempts,
        AIOptions options,
        ICollection<string> failures,
        string sectionName)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            failures.Add($"AI:Models:{sectionName}:{key}:Provider is required.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            failures.Add($"AI:Models:{sectionName}:{key}:Model is required.");
        }

        if (!string.IsNullOrWhiteSpace(provider)
            && !AIOptionsLookup.TryGetProvider(options, provider, out _))
        {
            failures.Add($"AI:Models:{sectionName}:{key}:Provider '{provider}' is not configured under AI:Providers.");
        }

        if (timeoutSeconds is <= 0)
        {
            failures.Add($"AI:Models:{sectionName}:{key}:TimeoutSeconds must be greater than 0 when provided.");
        }

        if (maxRetryAttempts is < 0)
        {
            failures.Add($"AI:Models:{sectionName}:{key}:MaxRetryAttempts cannot be negative.");
        }
    }
}