using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.OpenAI;

namespace Microsoft.Extensions.DependencyInjection;

public static class OpenAIBuilderExtensions
{
    public static AIBuilder AddOpenAI(this AIBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(OpenAIProviderDefaults.HttpClientName);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIChatProvider, OpenAIChatProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, OpenAIOptionsValidator>());
        return builder;
    }
}