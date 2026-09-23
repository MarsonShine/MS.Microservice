using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class ExternalIdentityTests
{
    private static readonly SymmetricSecurityKey Key = new(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());

    [Theory]
    [InlineData("profiles.manage", "profiles.manage", true)]
    [InlineData("read profiles.manage write", "profiles.manage", true)]
    [InlineData("  profiles.manage   ", "profiles.manage", true)]
    [InlineData("profiles.manage.extra", "profiles.manage", false)]
    [InlineData("PROFILES.MANAGE", "profiles.manage", false)]
    [InlineData("read\tprofiles.manage", "profiles.manage", false)]
    [InlineData("read\u00a0profiles.manage", "profiles.manage", false)]
    [InlineData("profiles.manage", "", false)]
    [InlineData("", "profiles.manage", false)]
    public void PermissionMatchesWholeSpaceDelimitedScopeWithOrdinalComparison(string scope, string permission, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", scope)]));

        Assert.Equal(expected, ExternalIdentityExtensions.HasPermission(principal, "scope", permission));
    }

    [Fact]
    public void PermissionSearchesAllClaimsAndIdentitiesWithClaimTypeMatching()
    {
        var principal = new ClaimsPrincipal([
            new ClaimsIdentity([new Claim("other", "profiles.manage"), new Claim("scope", "read")]),
            new ClaimsIdentity([new Claim("SCOPE", "write profiles.manage")])
        ]);

        Assert.True(ExternalIdentityExtensions.HasPermission(principal, "scope", "profiles.manage"));
        Assert.False(ExternalIdentityExtensions.HasPermission(principal, "other", "write"));
    }

    [Theory]
    [InlineData(null, "audience", HttpStatusCode.Unauthorized)]
    [InlineData("", "audience", HttpStatusCode.Forbidden)]
    [InlineData("profiles.manage", "audience", HttpStatusCode.OK)]
    [InlineData("profiles.manage", "wrong-audience", HttpStatusCode.Unauthorized)]
    [InlineData("PROFILES.MANAGE", "audience", HttpStatusCode.Forbidden)]
    public async Task ValidatedIdentityAndPermissionsControlTheRealPipeline(string? scope, string audience, HttpStatusCode expected)
    {
        var builder = ServiceHost.CreateBuilder(["--environment", "Production", "--Authentication:Authority", "https://issuer.example", "--Authentication:Audience", "audience"]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddExternalIdentity(builder.Configuration, builder.Environment);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
            configuration.SigningKeys.Add(Key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/managed", () => "ok").RequireAuthorization("Manage");
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (scope is not null)
        {
            var token = new JwtSecurityToken("https://issuer.example", audience,
                [new("sub", "external-user"), new("scope", scope)], expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        using var response = await client.GetAsync("/managed");
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("http://issuer.example", false, false)]
    [InlineData("http://issuer.example", true, true)]
    [InlineData("https://issuer.example", false, true)]
    [InlineData("invalid", true, false)]
    public void AuthorityValidationRespectsEnvironment(string authority, bool development, bool valid)
    {
        var options = new ExternalIdentityOptions { Authority = authority, Audience = "audience" };
        if (valid) options.Validate(development);
        else Assert.Throws<ArgumentException>(() => options.Validate(development));
    }

    [Theory]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", HttpStatusCode.Unauthorized)]
    [InlineData("unsigned", HttpStatusCode.Unauthorized)]
    [InlineData("empty-subject", HttpStatusCode.Forbidden)]
    public async Task InvalidTokenVariantsCannotAccessManagedRoutes(string variant, HttpStatusCode expected)
    {
        var builder = ServiceHost.CreateBuilder(["--environment", "Production", "--Authentication:Authority", "https://issuer.example", "--Authentication:Audience", "audience"]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration).AddExternalIdentity(builder.Configuration, builder.Environment);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
            configuration.SigningKeys.Add(Key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/managed", () => "ok").RequireAuthorization("Manage");
        await app.StartAsync();
        using var client = app.GetTestClient();
        var signingKey = variant == "wrong-key" ? new SymmetricSecurityKey(Enumerable.Repeat((byte)99, 32).ToArray()) : Key;
        var token = new JwtSecurityToken(variant == "wrong-issuer" ? "https://other.example" : "https://issuer.example", "audience",
            [new("sub", variant == "empty-subject" ? "" : "subject"), new("scope", "profiles.manage")],
            expires: DateTime.UtcNow.AddMinutes(variant == "expired" ? -5 : 5),
            signingCredentials: variant == "unsigned" ? null : new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using var response = await client.GetAsync("/managed");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledProblemsDoNotExposeExceptionDetails()
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapGet("/fail", (Func<string>)(() => throw new InvalidOperationException("private-payload")));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/fail");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private-payload", body);
        Assert.Contains("traceId", body);
    }
}
