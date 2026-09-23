using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace MS.Microservice.Messaging;

/// <summary>Shared contract alias used for persistence, RabbitMQ bindings, and Wolverine message types.</summary>
public static class MessageRoutingKey
{
    public const int MaxContractNameLength = 200;
    public const int MaxUtf8BytesExclusive = 255;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Format(string name, int version)
        => $"{name}.v{version.ToString(CultureInfo.InvariantCulture)}";

    public static bool IsValid([NotNullWhen(true)] string? name, int version)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxContractNameLength || version <= 0)
            return false;
        try { return Utf8.GetByteCount(Format(name, version)) < MaxUtf8BytesExclusive; }
        catch (EncoderFallbackException) { return false; }
    }
}
