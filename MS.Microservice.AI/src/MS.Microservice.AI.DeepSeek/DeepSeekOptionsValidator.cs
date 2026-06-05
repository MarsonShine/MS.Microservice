using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.DeepSeek;

public sealed class DeepSeekOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        return AIProviderRegistrationValidation.ValidateProvider(options, DeepSeekProviderDefaults.ProviderName, DeepSeekProviderDefaults.DefaultBaseAddress);
    }
}