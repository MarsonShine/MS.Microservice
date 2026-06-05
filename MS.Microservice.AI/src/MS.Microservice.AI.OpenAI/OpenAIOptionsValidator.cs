using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.AI.OpenAI;

public sealed class OpenAIOptionsValidator : IValidateOptions<AIOptions>
{
    public ValidateOptionsResult Validate(string? name, AIOptions options)
    {
        return AIProviderRegistrationValidation.ValidateProvider(options, OpenAIProviderDefaults.ProviderName, OpenAIProviderDefaults.DefaultBaseAddress);
    }
}