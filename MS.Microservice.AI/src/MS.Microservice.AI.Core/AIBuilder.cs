using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.AI.Core;

public sealed class AIBuilder
{
    public AIBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }
}