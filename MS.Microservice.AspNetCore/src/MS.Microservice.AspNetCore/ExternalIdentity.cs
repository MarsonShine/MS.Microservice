using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace MS.Microservice.AspNetCore;

public sealed class ExternalIdentityOptions
{
    private const string InvalidConfiguration = "External identity requires Authority, Audience and claim mappings; production Authority must use HTTPS.";

    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SubjectClaimType { get; set; } = "sub";
    public string RoleClaimType { get; set; } = "roles";
    public string PermissionClaimType { get; set; } = "scope";
    public string ManagePermission { get; set; } = "profiles.manage";
    public string OperationsPermission { get; set; } = "messaging.manage";

    public void Validate(bool development)
    {
        ValidateJwt(development);
        if (string.IsNullOrWhiteSpace(PermissionClaimType) || string.IsNullOrWhiteSpace(ManagePermission)
            || string.IsNullOrWhiteSpace(OperationsPermission))
            throw new ArgumentException(InvalidConfiguration);
    }

    internal void ValidateJwt(bool development)
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != "https" && !(development && authority.Scheme == "http"))
            || string.IsNullOrWhiteSpace(Audience) || string.IsNullOrWhiteSpace(SubjectClaimType)
            || string.IsNullOrWhiteSpace(RoleClaimType))
            throw new ArgumentException(InvalidConfiguration);
    }
}

public static partial class ExternalIdentityExtensions
{
    /// <summary>Registers external JWT validation and subject-based default and fallback authorization; hosts supply named policies.</summary>
    public static IServiceCollection AddExternalJwtIdentity(this IServiceCollection services, ExternalIdentityOptions identity,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(environment);
        identity.ValidateJwt(environment.IsDevelopment());
        services.AddSingleton(identity);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = identity.Authority;
            options.Audience = identity.Audience;
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                NameClaimType = identity.SubjectClaimType, RoleClaimType = identity.RoleClaimType,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .RequireAssertion(context => HasSubject(context.User, identity.SubjectClaimType)).Build();
            options.FallbackPolicy = options.DefaultPolicy;
        });
        return services;
    }

    /// <summary>Requires an authenticated identity with one distinct nonempty subject and the specified whole scope token.</summary>
    public static AuthorizationPolicyBuilder RequireExternalScope(this AuthorizationPolicyBuilder policy,
        ExternalIdentityOptions identity, string permission)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.SubjectClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.PermissionClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return policy.RequireAuthenticatedUser()
            .RequireAssertion(context => HasSubject(context.User, identity.SubjectClaimType))
            .RequireAssertion(context => HasPermission(context.User, identity.PermissionClaimType, permission));
    }

    private static bool HasSubject(ClaimsPrincipal user, string claimType)
    {
        var values = user.FindAll(claimType).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 && !string.IsNullOrWhiteSpace(values[0]);
    }

    public static bool HasPermission(ClaimsPrincipal user, string claimType, string permission)
    {
        if (string.IsNullOrEmpty(permission)) return false;

        foreach (var claim in user.FindAll(claimType))
        {
            var remaining = claim.Value.AsSpan();
            while (!remaining.IsEmpty)
            {
                var separator = remaining.IndexOf(' ');
                var scope = separator < 0 ? remaining : remaining[..separator];
                if (!scope.IsEmpty && scope.Equals(permission, StringComparison.Ordinal)) return true;
                if (separator < 0) break;
                remaining = remaining[(separator + 1)..];
            }
        }

        return false;
    }
}
