using System.Security.Claims;
using MS.Microservice.AspNetCore;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web;

internal static class ReferenceActor
{
    internal static AuditActor From(ClaimsPrincipal user, ExternalIdentityOptions identity)
        => new(user.FindFirstValue("iss") ?? "", user.FindFirstValue(identity.SubjectClaimType) ?? "");
}
