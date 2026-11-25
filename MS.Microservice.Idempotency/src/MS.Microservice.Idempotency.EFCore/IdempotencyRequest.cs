using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Idempotency.EFCore;

/// <summary>Identifies one authenticated operation and the exact request representation being retried.</summary>
public sealed class IdempotencyRequest
{
    public const int MaximumKeyLength = 128;
    public const int MaximumRequestBytes = 1024 * 1024;
    private static readonly SearchValues<char> OperationCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    internal string ScopeHash { get; }
    internal string KeyHash { get; }
    internal string RequestHash { get; }

    private IdempotencyRequest(string scopeHash, string keyHash, string requestHash)
        => (ScopeHash, KeyHash, RequestHash) = (scopeHash, keyHash, requestHash);

    /// <remarks>
    /// The caller must pass one stable operation name, the authenticated actor identity, and a canonical
    /// request representation. It must reject multiple Idempotency-Key header values before calling this method.
    /// </remarks>
    public static IdempotencyRequest Create(string operation, string actor, string key,
        ReadOnlySpan<byte> requestRepresentation)
    {
        if (string.IsNullOrEmpty(operation) || operation.Length > 128 || operation.AsSpan().ContainsAnyExcept(OperationCharacters))
        {
            throw new ArgumentException("Operation must contain 1..128 ASCII letters, digits, '.', '_' or '-'.", nameof(operation));
        }
        var actorBytes = Encoding.UTF8.GetByteCount(actor.AsSpan());
        if (string.IsNullOrWhiteSpace(actor) || actorBytes > 1024)
        {
            throw new ArgumentException("Actor must contain 1..1024 UTF-8 bytes.", nameof(actor));
        }
        if (string.IsNullOrEmpty(key) || key.Length > MaximumKeyLength ||
            key.AsSpan().ContainsAnyExceptInRange('!', '~') || key.Contains(','))
        {
            throw new ArgumentException("Key must contain 1..128 visible ASCII characters without commas.", nameof(key));
        }
        if (requestRepresentation.Length > MaximumRequestBytes)
            throw new ArgumentOutOfRangeException(nameof(requestRepresentation));

        // Operation and key are ASCII. The persisted scope format is UTF8(operation), NUL, UTF8(actor).
        var scopeLength = operation.Length + 1 + actorBytes;
        Span<byte> buffer = stackalloc byte[Math.Max(scopeLength, key.Length)];
        Encoding.UTF8.GetBytes(operation, buffer);
        buffer[operation.Length] = 0;
        Encoding.UTF8.GetBytes(actor, buffer[(operation.Length + 1)..]);
        var scopeHash = Hash(buffer[..scopeLength]);
        Encoding.UTF8.GetBytes(key, buffer);
        return new IdempotencyRequest(scopeHash, Hash(buffer[..key.Length]), Hash(requestRepresentation));
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexString(digest);
    }
}
