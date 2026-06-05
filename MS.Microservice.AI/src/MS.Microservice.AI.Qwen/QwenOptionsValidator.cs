using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.Qwen;

public sealed class QwenOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        return AIProviderRegistrationValidation.ValidateProvider(options, QwenProviderDefaults.ProviderName, QwenProviderDefaults.DefaultBaseAddress);
    }
}