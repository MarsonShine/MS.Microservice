using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Idempotency.EFCore;

/// <summary>Identifies one authenticated operation and the exact request representation being retried.</summary>
public sealed class IdempotencyRequest
{
    public const int MaximumKeyLength = 128;
    public const int MaximumRequestBytes = 1024 * 1024;

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
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 128 ||
            !operation.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            throw new ArgumentException("Operation must contain 1..128 ASCII letters, digits, '.', '_' or '-'.", nameof(operation));
        if (string.IsNullOrWhiteSpace(actor) || Encoding.UTF8.GetByteCount(actor) > 1024)
            throw new ArgumentException("Actor must contain 1..1024 UTF-8 bytes.", nameof(actor));
        if (string.IsNullOrEmpty(key) || key.Length > MaximumKeyLength ||
            !key.All(static character => character is >= '!' and <= '~' and not ','))
            throw new ArgumentException("Key must contain 1..128 visible ASCII characters without commas.", nameof(key));
        if (requestRepresentation.Length > MaximumRequestBytes)
            throw new ArgumentOutOfRangeException(nameof(requestRepresentation));

        return new IdempotencyRequest(
            Hash(Encoding.UTF8.GetBytes(operation + "\0" + actor)),
            Hash(Encoding.UTF8.GetBytes(key)),
            Hash(requestRepresentation));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
