using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.DeepSeek;

public sealed class DeepSeekOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        var baseResult = AIProviderRegistrationValidation.ValidateProvider(options, DeepSeekProviderDefaults.ProviderName, DeepSeekProviderDefaults.DefaultBaseAddress);
        if (baseResult.Failed)
        {
            return baseResult;
        }

        var failures = new List<string>();
        AddUnsupportedCapabilityFailures(options.Models.Tts, failures, "Tts");
        AddUnsupportedCapabilityFailures(options.Models.Asr, failures, "Asr");
        AddUnsupportedCapabilityFailures(options.Models.ImageGeneration, failures, "ImageGeneration");
        AddUnsupportedCapabilityFailures(options.Models.ImageEdit, failures, "ImageEdit");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddUnsupportedCapabilityFailures<TModelOptions>(
        IDictionary<string, TModelOptions> models,
        ICollection<string> failures,
        string capabilitySection)
        where TModelOptions : class
    {
        foreach (var model in models)
        {
            var providerProperty = typeof(TModelOptions).GetProperty("Provider");
            var provider = providerProperty?.GetValue(model.Value) as string;
            if (string.Equals(provider, DeepSeekProviderDefaults.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"AI:Models:{capabilitySection}:{model.Key} cannot use provider '{DeepSeekProviderDefaults.ProviderName}' because it currently supports chat only.");
            }
        }
    }
}