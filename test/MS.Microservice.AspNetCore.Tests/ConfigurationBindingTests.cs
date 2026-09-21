using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class ConfigurationBindingTests
{
    [Fact]
    public void ProxyAndOriginArraysPreserveIndexedOrderAndDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:KnownProxies:0"] = "192.0.2.10",
            ["Http:KnownProxies:2"] = "2001:db8::1",
            ["Cors:Origins:0"] = "https://first.example",
            ["Cors:Origins:2"] = "https://second.example"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging().AddPlatformHttp(configuration);
        using var provider = services.BuildServiceProvider();

        var forwarded = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        foreach (var defaultProxy in new ForwardedHeadersOptions().KnownProxies)
            Assert.Contains(defaultProxy, forwarded.KnownProxies);
        Assert.Equal(new[] { IPAddress.Parse("192.0.2.10"), IPAddress.Parse("2001:db8::1") },
            forwarded.KnownProxies.TakeLast(2));
        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        Assert.Equal(new[] { "https://first.example", "https://second.example" }, cors.GetPolicy(cors.DefaultPolicyName)!.Origins);
    }

    [Fact]
    public void MissingArraysKeepFrameworkDefaultsAndDoNotAllowAnyOrigin()
    {
        var services = new ServiceCollection();
        services.AddLogging().AddPlatformHttp(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var forwarded = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(new ForwardedHeadersOptions().KnownProxies, forwarded.KnownProxies);
        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        Assert.Empty(cors.GetPolicy(cors.DefaultPolicyName)!.Origins);
        Assert.False(cors.GetPolicy(cors.DefaultPolicyName)!.AllowAnyOrigin);
    }

    [Fact]
    public void MalformedProxyStillFailsWhenForwardingOptionsAreResolved()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:KnownProxies:0"] = "not-an-address"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging().AddPlatformHttp(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<FormatException>(() => provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityBindingPreservesDefaultsOrConfiguredClaimMappings(bool customClaims)
    {
        var builder = ServiceHost.CreateBuilder(["--environment", "Development"]);
        builder.Configuration["authentication:authority"] = "http://issuer.example";
        builder.Configuration["authentication:audience"] = "configured-audience";
        if (customClaims)
        {
            builder.Configuration["Authentication:SubjectClaimType"] = "user_id";
            builder.Configuration["Authentication:RoleClaimType"] = "groups";
            builder.Configuration["Authentication:PermissionClaimType"] = "permissions";
            builder.Configuration["Authentication:ManagePermission"] = "custom.manage";
            builder.Configuration["Authentication:OperationsPermission"] = "custom.operations";
        }
        builder.Services.AddExternalIdentity(builder.Configuration, builder.Environment);
        using var provider = builder.Services.BuildServiceProvider();
        var identity = provider.GetRequiredService<ExternalIdentityOptions>();
        Assert.Equal(customClaims ? "user_id" : "sub", identity.SubjectClaimType);
        Assert.Equal(customClaims ? "groups" : "roles", identity.RoleClaimType);
        Assert.Equal(customClaims ? "permissions" : "scope", identity.PermissionClaimType);
        Assert.Equal(customClaims ? "custom.manage" : "profiles.manage", identity.ManagePermission);
        Assert.Equal(customClaims ? "custom.operations" : "messaging.manage", identity.OperationsPermission);
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("http://issuer.example", bearer.Authority);
        Assert.Equal("configured-audience", bearer.Audience);
        Assert.False(bearer.RequireHttpsMetadata);
        Assert.Equal(identity.SubjectClaimType, bearer.TokenValidationParameters.NameClaimType);
        Assert.Equal(identity.RoleClaimType, bearer.TokenValidationParameters.RoleClaimType);
    }
}
