using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class ExternalJwtIdentityTests
{
    private static readonly SymmetricSecurityKey Key = new(Enumerable.Repeat((byte)29, 32).ToArray());

    [Fact]
    public async Task PureJwtRegistrationSharesOptionsAndRequiresNoPermissionSettingsOrReferencePolicies()
    {
        var builder = CreateBuilder();
        var identity = Identity();
        identity.PermissionClaimType = "";
        identity.ManagePermission = "";
        identity.OperationsPermission = "";
        builder.Services.AddExternalJwtIdentity(identity, builder.Environment);
        ConfigureSigningKeys(builder.Services);
        await using var app = builder.Build();
        Assert.Same(identity, app.Services.GetRequiredService<ExternalIdentityOptions>());
        var authorization = app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.Null(authorization.GetPolicy("Manage"));
        Assert.Null(authorization.GetPolicy("MessagingOperations"));
        Assert.Same(authorization.DefaultPolicy, authorization.FallbackPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/identity", () => "ok");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var anonymous = await client.GetAsync("/identity");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Authenticate(client, [new(identity.SubjectClaimType, "external-user")]);
        using var authenticated = await client.GetAsync("/identity");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("duplicate-subject", HttpStatusCode.OK)]
    [InlineData("missing-subject", HttpStatusCode.Forbidden)]
    [InlineData("empty-subject", HttpStatusCode.Forbidden)]
    [InlineData("conflicting-subject", HttpStatusCode.Forbidden)]
    [InlineData("scope-prefix", HttpStatusCode.Forbidden)]
    [InlineData("scope-case", HttpStatusCode.Forbidden)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-audience", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", HttpStatusCode.Unauthorized)]
    [InlineData("unsigned", HttpStatusCode.Unauthorized)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    public async Task HostScopePolicyPreservesJwtSubjectAndScopeChecks(string variant, HttpStatusCode expected)
    {
        var builder = CreateBuilder();
        var identity = Identity();
        identity.ManagePermission = "";
        identity.OperationsPermission = "";
        builder.Services.AddExternalJwtIdentity(identity, builder.Environment);
        var policy = new AuthorizationPolicyBuilder().RequireExternalScope(identity, "reports.read").Build();
        builder.Services.AddAuthorization(options => options.AddPolicy("Reports", policy));
        ConfigureSigningKeys(builder.Services);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/reports", () => "ok").RequireAuthorization("Reports");
        await app.StartAsync();
        using var client = app.GetTestClient();
        var permission = variant switch
        {
            "scope-prefix" => "reports.read.extra", "scope-case" => "REPORTS.READ", _ => "other reports.read"
        };
        var claims = new List<Claim> { new(identity.PermissionClaimType, permission) };
        if (variant != "missing-subject")
            claims.Add(new(identity.SubjectClaimType, variant == "empty-subject" ? "" : "external-user"));
        if (variant is "duplicate-subject" or "conflicting-subject")
            claims.Add(new(identity.SubjectClaimType, variant == "duplicate-subject" ? "external-user" : "another-user"));
        if (variant != "anonymous") Authenticate(client, claims, variant);

        using var response = await client.GetAsync("/reports");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ScopePolicyRejectsUnauthenticatedIdentityEvenWithMatchingClaims()
    {
        var builder = CreateBuilder();
        var identity = Identity();
        builder.Services.AddExternalJwtIdentity(identity, builder.Environment);
        var policy = new AuthorizationPolicyBuilder().RequireExternalScope(identity, "reports.read").Build();
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(identity.SubjectClaimType, "external-user"), new(identity.PermissionClaimType, "reports.read")]));

        Assert.False((await authorization.AuthorizeAsync(principal, null, policy)).Succeeded);
    }

    [Theory]
    [InlineData("Authority")]
    [InlineData("Audience")]
    [InlineData("SubjectClaimType")]
    [InlineData("RoleClaimType")]
    public void GenericRegistrationStillValidatesJwtSettings(string setting)
    {
        var builder = CreateBuilder();
        var identity = Identity();
        switch (setting)
        {
            case "Authority": identity.Authority = "http://issuer.example"; break;
            case "Audience": identity.Audience = ""; break;
            case "SubjectClaimType": identity.SubjectClaimType = ""; break;
            case "RoleClaimType": identity.RoleClaimType = ""; break;
        }
        Assert.Throws<ArgumentException>(() => builder.Services.AddExternalJwtIdentity(identity, builder.Environment));
    }

    [Theory]
    [InlineData("", "permissions", "reports.read")]
    [InlineData("subject_id", "", "reports.read")]
    [InlineData("subject_id", "permissions", " ")]
    public void ScopeConfigurationIsValidatedWhenThePolicyIsBuilt(string subject, string claim, string permission)
    {
        var identity = Identity();
        identity.SubjectClaimType = subject;
        identity.PermissionClaimType = claim;
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicyBuilder().RequireExternalScope(identity, permission));
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = ServiceHost.CreateBuilder(["--environment", "Production"]);
        builder.WebHost.UseTestServer();
        return builder;
    }

    private static ExternalIdentityOptions Identity() => new()
    {
        Authority = "https://issuer.example", Audience = "audience",
        SubjectClaimType = "subject_id", PermissionClaimType = "permissions"
    };

    private static void ConfigureSigningKeys(IServiceCollection services)
        => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
            configuration.SigningKeys.Add(Key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });

    private static void Authenticate(HttpClient client, IEnumerable<Claim> claims, string variant = "valid")
    {
        var signingKey = variant == "wrong-key" ? new SymmetricSecurityKey(Enumerable.Repeat((byte)99, 32).ToArray()) : Key;
        var token = new JwtSecurityToken(
            variant == "wrong-issuer" ? "https://other.example" : "https://issuer.example",
            variant == "wrong-audience" ? "other-audience" : "audience", claims,
            expires: DateTime.UtcNow.AddMinutes(variant == "expired" ? -5 : 5),
            signingCredentials: variant == "unsigned" ? null : new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
