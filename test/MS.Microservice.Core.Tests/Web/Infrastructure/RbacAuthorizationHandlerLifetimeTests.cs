using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Lab.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Lab.Infrastructure.Extensions;
using NSubstitute;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class RbacAuthorizationHandlerLifetimeTests
{
    [Fact]
    public void AddCustomAuthentication_RegistersRbacHandlerAsScoped()
    {
        var services = CreateServices();

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(IAuthorizationHandler)
                && service.ImplementationType == typeof(RbacAuthorizationHandler));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void RbacAuthorizationHandler_IsReusedWithinScopeAndIsolatedBetweenScopes()
    {
        var services = CreateServices();
        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        using var firstScope = serviceProvider.CreateScope();
        var firstHandler = GetRbacHandler(firstScope.ServiceProvider);
        var sameScopeHandler = GetRbacHandler(firstScope.ServiceProvider);

        using var secondScope = serviceProvider.CreateScope();
        var secondHandler = GetRbacHandler(secondScope.ServiceProvider);

        Assert.Same(firstHandler, sameScopeHandler);
        Assert.NotSame(firstHandler, secondHandler);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IUserDomainService>());
        services.AddScoped(_ => Substitute.For<IDistributedCache>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityOptions:JwtBearerOption:Audiences:0"] = "test-audience",
                ["IdentityOptions:JwtBearerOption:Issuers:0"] = "test-issuer",
                ["IdentityOptions:JwtBearerOption:SecurityKeys:0"] = "external-jwt-security-key-32-characters-long",
                ["IdentityOptions:JwtBearerOption:Expires"] = "3600"
            })
            .Build();

        services.AddCustomConfiguration(configuration);
        services.AddCustomAuthentication(configuration);
        return services;
    }

    private static RbacAuthorizationHandler GetRbacHandler(IServiceProvider serviceProvider)
        => serviceProvider
            .GetServices<IAuthorizationHandler>()
            .OfType<RbacAuthorizationHandler>()
            .Single();
}
