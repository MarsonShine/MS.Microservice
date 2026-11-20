using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MS.Microservice.AspNetCore;

public static partial class ExternalIdentityExtensions
{
    /// <summary>Compatibility registration retaining the Manage and MessagingOperations policies and their configuration.</summary>
    public static IServiceCollection AddExternalIdentity(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var identity = configuration.GetSection("Authentication").Get<ExternalIdentityOptions>() ?? new();
        identity.Validate(environment.IsDevelopment());
        services.AddExternalJwtIdentity(identity, environment);
        services.AddAuthorization(options =>
        {
            // Existing callers may mutate the registered options; retain the original policy closures.
            options.AddPolicy("Manage", policy => policy.RequireAuthenticatedUser().RequireAssertion(context => HasSubject(context.User, identity.SubjectClaimType))
                .RequireAssertion(context => HasPermission(context.User, identity.PermissionClaimType, identity.ManagePermission)));
            options.AddPolicy("MessagingOperations", policy => policy.RequireAuthenticatedUser().RequireAssertion(context => HasSubject(context.User, identity.SubjectClaimType))
                .RequireAssertion(context => HasPermission(context.User, identity.PermissionClaimType, identity.OperationsPermission)));
        });
        return services;
    }
}
