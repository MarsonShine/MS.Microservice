using Microsoft.Extensions.Options;
using MS.Microservice.AI.Abstractions;

namespace MS.Microservice.AI.Core;

/// <summary>Shares provider permits across built-in capabilities within one service container.</summary>
internal sealed class AIProviderConcurrency(IOptions<AIOptions> options)
{
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public SemaphoreSlim GetRequired(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        lock (_gates)
        {
            if (_gates.TryGetValue(providerName, out var gate)) return gate;
            if (!AIOptionsLookup.TryGetProvider(options.Value, providerName, out var registration))
                throw new AIConfigurationException($"AI provider '{providerName}' is not configured under AI:Providers.");
            gate = new SemaphoreSlim(registration.ConcurrencyLimit, registration.ConcurrencyLimit);
            _gates.Add(providerName, gate);
            return gate;
        }
    }
}
