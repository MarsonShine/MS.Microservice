using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.AspNetCore;
using MS.Microservice.Reference.Application;

namespace MS.Microservice.Reference.Web.HttpIdempotency;

internal interface IIdempotencyActorScope
{
    string? Resolve(ClaimsPrincipal user);
}

internal sealed class ReferenceIdempotencyActorScope(ExternalIdentityOptions identity) : IIdempotencyActorScope
{
    public string? Resolve(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        var actor = ReferenceActor.From(user, identity);
        if (string.IsNullOrWhiteSpace(actor.Issuer) || string.IsNullOrWhiteSpace(actor.Subject)) return null;

        // Token refresh must not change the authenticated actor's key scope.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(actor,
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
