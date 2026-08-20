using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Web.Infrastructure.Extensions;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class JwtSecurityKeyConfigurationTests
{
    [Fact]
    public void AddCustomAuthentication_WhenSecurityKeysAreMissing_ThrowsOptionsValidationException()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        var exception = Assert.Throws<OptionsValidationException>(
            () => services.AddCustomAuthentication(configuration));

        Assert.Contains(
            "IdentityOptions:JwtBearerOption:SecurityKeys is required.",
            exception.Failures);
    }

    [Theory]
    [InlineData("short-key")]
    [InlineData("这是一个长度超过三十二个字符但不是ASCII的JWT签名密钥")]
    public void AddCustomAuthentication_WhenSecurityKeyIsInvalid_ThrowsOptionsValidationException(string securityKey)
    {
        var configuration = BuildConfiguration(securityKey);
        var services = new ServiceCollection();

        var exception = Assert.Throws<OptionsValidationException>(
            () => services.AddCustomAuthentication(configuration));

        Assert.Contains(
            "IdentityOptions:JwtBearerOption:SecurityKeys entries must contain at least 32 ASCII characters.",
            exception.Failures);
    }

    [Fact]
    public void AddCustomAuthentication_WithHierarchicalExternalKeys_BindsValidatedOptions()
    {
        var securityKeys = new[]
        {
            "first-external-jwt-key-32-characters-long",
            "second-external-jwt-key-32-characters-long"
        };
        var configuration = BuildConfiguration(securityKeys);
        var services = new ServiceCollection();

        services.AddCustomConfiguration(configuration);
        services.AddCustomAuthentication(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(securityKeys, options.JwtBearerOption!.SecurityKeys);
    }

    private static IConfiguration BuildConfiguration(params string[] securityKeys)
    {
        var values = new Dictionary<string, string?>
        {
            ["IdentityOptions:JwtBearerOption:Audiences:0"] = "test-audience",
            ["IdentityOptions:JwtBearerOption:Issuers:0"] = "test-issuer",
            ["IdentityOptions:JwtBearerOption:Expires"] = "3600"
        };

        for (var index = 0; index < securityKeys.Length; index++)
        {
            values[$"IdentityOptions:JwtBearerOption:SecurityKeys:{index}"] = securityKeys[index];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
