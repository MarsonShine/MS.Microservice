using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.AspNetCore;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web;

internal interface IIdempotencyActorScope
{
    string? Resolve(ClaimsPrincipal user);
}

internal sealed class ReferenceIdempotencyActorScope(ExternalIdentityOptions identity) : IIdempotencyActorScope
{
    public string? Resolve(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        var issuer = user.FindFirstValue("iss");
        var subject = user.FindFirstValue(identity.SubjectClaimType);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)) return null;

        // Preserve the scope format used by existing records while taking identity from validated claims.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new AuditActor(issuer, subject),
            ReferenceIdempotencyJsonContext.Default.AuditActor);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
