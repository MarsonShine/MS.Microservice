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
            if (string.IsNullOrWhiteSpace(model.Value.Provider))
            {
                failures.Add($"AI:Models:Chat:{model.Key}:Provider is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Value.Model))
            {
                failures.Add($"AI:Models:Chat:{model.Key}:Model is required.");
            }

            if (!string.IsNullOrWhiteSpace(model.Value.Provider)
                && !AIOptionsLookup.TryGetProvider(options, model.Value.Provider, out _))
            {
                failures.Add($"AI:Models:Chat:{model.Key}:Provider '{model.Value.Provider}' is not configured under AI:Providers.");
            }

            if (model.Value.TimeoutSeconds is <= 0)
            {
                failures.Add($"AI:Models:Chat:{model.Key}:TimeoutSeconds must be greater than 0 when provided.");
            }

            if (model.Value.MaxRetryAttempts is < 0)
            {
                failures.Add($"AI:Models:Chat:{model.Key}:MaxRetryAttempts cannot be negative.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}