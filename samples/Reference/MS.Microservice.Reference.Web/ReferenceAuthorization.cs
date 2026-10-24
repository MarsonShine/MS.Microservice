using Microsoft.AspNetCore.Authorization;
using MS.Microservice.AspNetCore;

namespace MS.Microservice.Reference.Web;

internal static class ReferenceAuthorization
{
    internal static IServiceCollection AddReferenceAuthorization(this IServiceCollection services, ExternalIdentityOptions identity)
    {
        var manage = new AuthorizationPolicyBuilder().RequireExternalScope(identity, identity.ManagePermission).Build();
        var operations = new AuthorizationPolicyBuilder().RequireExternalScope(identity, identity.OperationsPermission).Build();
        return services.AddAuthorization(options =>
        {
            options.AddPolicy("Manage", manage);
            options.AddPolicy("MessagingOperations", operations);
        });
    }
}
