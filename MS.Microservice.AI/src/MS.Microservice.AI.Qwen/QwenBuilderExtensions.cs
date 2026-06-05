using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;
using MS.Microservice.AI.Core;
using MS.Microservice.AI.Qwen;

namespace Microsoft.Extensions.DependencyInjection;

public static class QwenBuilderExtensions
{
    public static AIBuilder AddQwen(this AIBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(QwenProviderDefaults.HttpClientName);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIChatProvider, QwenChatProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AIOptions>, QwenOptionsValidator>());
        return builder;
    }
}