using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.DeepSeek;

namespace Microsoft.Extensions.DependencyInjection;

public static class DeepSeekBuilderExtensions
{
    public static AIBuilder AddDeepSeek(this AIBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(DeepSeekProviderDefaults.HttpClientName);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIChatProvider, DeepSeekChatProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, DeepSeekOptionsValidator>());
        return builder;
    }
}