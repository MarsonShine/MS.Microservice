using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal interface IIdempotencyLookup
{
    Task<IdempotencyLookup> FindAsync(IdempotencyRequest request, string operation, string path,
        string actorHash, string key, string? contentType, ReadOnlyMemory<byte> body, CancellationToken token);
}

// Temporary compatibility for records written with the old bound-CreateProfile fingerprint.
internal sealed class ReferenceLegacyProfileIdempotencyLookup(
    EfCoreIdempotencyStore<ReferenceDbContext> store) : IIdempotencyLookup
{
    public async Task<IdempotencyLookup> FindAsync(IdempotencyRequest request, string operation, string path,
        string actorHash, string key, string? contentType, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        var found = await store.FindAsync(request, token);
        if (found.Kind != IdempotencyLookupKind.DifferentRequest ||
            operation != "profiles.create" ||
            !string.Equals(path, "/api/v1/profiles", StringComparison.OrdinalIgnoreCase) ||
            !ReferenceHttpIdempotencyExecutor.IsJson(ReferenceHttpIdempotencyExecutor.MediaType(contentType)))
            return found;

        try
        {
            using var document = ReferenceHttpIdempotencyExecutor.ParseJson(body, contentType);
            var profile = document.RootElement.Deserialize(ReferenceIdempotencyJsonContext.Default.CreateProfile);
            if (profile is null) return found;
            var legacyBody = JsonSerializer.SerializeToUtf8Bytes(profile,
                ReferenceIdempotencyJsonContext.Default.CreateProfile);
            try
            {
                var legacyRequest = IdempotencyRequest.Create(operation, actorHash, key, legacyBody);
                var legacy = await store.FindAsync(legacyRequest, token);
                return legacy.Kind == IdempotencyLookupKind.Replay ? legacy : found;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(legacyBody);
            }
        }
        catch (JsonException)
        {
            return found;
        }
    }
}
