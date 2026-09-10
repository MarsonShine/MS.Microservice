using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Lab.Application.Identity.Token;
using MS.Microservice.Lab.Infrastructure.Extensions;
using Xunit;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class LabTokenRoundTripTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExplicitIssuerWorksWithZeroOrSeveralAdditionalTrustedKeys(int extraKeys)
    {
        const string activeKey = "active-lab-signing-key-at-least-32-characters";
        var values = new Dictionary<string, string?>
        {
            ["LabTokenIssuer:Issuer"] = "http://lab.test", ["LabTokenIssuer:Audience"] = "ms-lab",
            ["LabTokenIssuer:SigningKey"] = activeKey
        };
        for (var index = 0; index < extraKeys; index++) values[$"IdentityOptions:JwtBearerOption:SecurityKeys:{index}"] = $"old-lab-signing-key-number-{index}-at-least-32-characters";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddCustomConfiguration(configuration);
        services.AddCustomAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        var generator = new BearerTokenGenerator(provider.GetRequiredService<IOptions<LabTokenIssuerOptions>>(), TimeProvider.System);
        var user = new User("account", "unused", "unused", false, "phone", 1, 1, "mail@example.test", "name", "", "") { Id = 42 };
        var token = await generator.Generate(user);
        var validation = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, validation, out var parsed);
        Assert.Equal("42", principal.FindFirst("sub")!.Value);
        Assert.Equal("http://lab.test", parsed.Issuer);
    }
}
