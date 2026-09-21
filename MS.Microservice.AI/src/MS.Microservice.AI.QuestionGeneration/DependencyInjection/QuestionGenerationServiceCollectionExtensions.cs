using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.QuestionGeneration.AIChat;
using MS.Microservice.AI.QuestionGeneration.Contracts;
using MS.Microservice.AI.QuestionGeneration.Pipeline;
using MS.Microservice.AI.QuestionGeneration.Prompts;
using MS.Microservice.AI.QuestionGeneration.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

public static class QuestionGenerationServiceCollectionExtensions
{
    public static QuestionGenerationBuilder AddQuestionGeneration(
        this IServiceCollection services,
        Action<QuestionGenerationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<QuestionGenerationOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(
                value =>
                    !string.IsNullOrWhiteSpace(value.DraftScenario) &&
                    !string.IsNullOrWhiteSpace(value.ReviewScenario) &&
                    !string.IsNullOrWhiteSpace(value.RepairScenario),
                "Question generation model scenarios cannot be empty.")
            .ValidateOnStart();

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IQuestionJsonContract, SystemTextJsonQuestionContract>();
        services.TryAddSingleton<IQuestionPromptProvider, DefaultQuestionPromptProvider>();
        services.TryAddSingleton<IQuestionDuplicateDetector, ExactQuestionDuplicateDetector>();
        services.TryAddSingleton<IQuestionModelClient, AIChatQuestionModelClient>();
        services.TryAddSingleton<QuestionDefinitionRegistry>();
        services.TryAddTransient<IQuestionGenerationHarness, QuestionGenerationHarness>();
        return new(services);
    }
}

public sealed class QuestionGenerationBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>Registers generated metadata for a host-owned response type.</summary>
    public QuestionGenerationBuilder AddJsonTypeInfo<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        Services.AddSingleton<JsonTypeInfo>(typeInfo);
        return this;
    }

    public QuestionGenerationBuilder AddDefinition<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDefinition>()
        where TDefinition : class, IQuestionDefinition
    {
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IQuestionDefinition, TDefinition>());
        return this;
    }
}
