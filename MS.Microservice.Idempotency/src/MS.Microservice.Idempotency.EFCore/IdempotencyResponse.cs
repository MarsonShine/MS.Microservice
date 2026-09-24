namespace MS.Microservice.Idempotency.EFCore;

/// <summary>The bounded, stable parts of an HTTP response that can be replayed after a committed write.</summary>
public sealed class IdempotencyResponse
{
    public const int MaximumBodyBytes = 64 * 1024;
    private readonly byte[] _body;

    public int StatusCode { get; }
    public string? ContentType { get; }
    public string? Location { get; }
    public ReadOnlyMemory<byte> Body => _body;

    public IdempotencyResponse(int statusCode, string? contentType, ReadOnlySpan<byte> body, string? location = null)
    {
        if (statusCode is < 200 or >= 500)
            throw new ArgumentOutOfRangeException(nameof(statusCode), "Only completed non-server-error responses can be stored.");
        if (contentType is not null &&
            (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || contentType.Any(char.IsControl)))
            throw new ArgumentException("Content type must contain 1..128 non-control characters when present.", nameof(contentType));
        if (location is not null && (location.Length > 2048 || location.Any(char.IsControl)))
            throw new ArgumentException("Location must contain at most 2048 non-control characters.", nameof(location));
        if (body.Length > MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body));

        StatusCode = statusCode;
        ContentType = contentType;
        Location = location;
        _body = body.ToArray();
    }

    internal byte[] CopyBody() => (byte[])_body.Clone();
}
