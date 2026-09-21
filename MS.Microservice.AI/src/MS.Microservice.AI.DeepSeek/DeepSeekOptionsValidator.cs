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
        AddUnsupportedCapabilityFailures(options.Models.Tts, static model => model.Provider, failures, "Tts");
        AddUnsupportedCapabilityFailures(options.Models.Asr, static model => model.Provider, failures, "Asr");
        AddUnsupportedCapabilityFailures(options.Models.ImageGeneration, static model => model.Provider, failures, "ImageGeneration");
        AddUnsupportedCapabilityFailures(options.Models.ImageEdit, static model => model.Provider, failures, "ImageEdit");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddUnsupportedCapabilityFailures<TModelOptions>(
        IDictionary<string, TModelOptions> models,
        Func<TModelOptions, string?> getProvider,
        ICollection<string> failures,
        string capabilitySection)
        where TModelOptions : class
    {
        foreach (var model in models)
        {
            var provider = getProvider(model.Value);
            if (string.Equals(provider, DeepSeekProviderDefaults.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"AI:Models:{capabilitySection}:{model.Key} cannot use provider '{DeepSeekProviderDefaults.ProviderName}' because it currently supports chat only.");
            }
        }
    }
}