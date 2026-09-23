using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MS.Microservice.Http.Resilience;

public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Adds the .NET standard HTTP resilience pipeline to one named or typed client.
    /// Unsafe HTTP methods are never retried by this registration.
    /// </summary>
    public static IHttpClientBuilder AddMsHttpResilience(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStandardResilienceHandler(options =>
        {
            configure?.Invoke(options);
            options.Retry.DisableForUnsafeHttpMethods();
        });
        return builder;
    }
}
