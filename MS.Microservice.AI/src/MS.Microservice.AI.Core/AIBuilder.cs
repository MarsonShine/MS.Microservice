using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.AI.Core;

/// <summary>
/// Fluent builder returned by <c>AddMicroserviceAI(...)</c>.
/// Call provider-specific extension methods such as <c>.AddOpenAI()</c>,
/// <c>.AddDeepSeek()</c>, or <c>.AddQwen()</c> to register concrete providers.
/// </summary>
public sealed class AIBuilder
{
    /// <summary>
    /// Initializes a new instance of <see cref="AIBuilder"/>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    public AIBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>The application service collection for further registration.</summary>
    public IServiceCollection Services { get; }
}