using System.Globalization;
using System.Text;
using RabbitMQ.Client;

namespace MS.Microservice.Messaging.RabbitMQ;

internal static class RabbitMqWireCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string RoutingKey(SerializedMessage message) => $"{message.ContractName}.v{message.ContractVersion}";

    public static (BasicProperties Properties, byte[] Body) Encode(SerializedMessage message, int maxBytes)
    {
        if (message.Id == Guid.Empty || message.OccurredAtUtc == default || message.OccurredAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(message.ContractName) || message.ContractVersion <= 0)
            throw new PermanentMessageException("invalid_message_metadata");
        if (!MessageMetadataLimits.AreValid(message.CorrelationId, message.TraceParent, message.TraceState))
            throw new PermanentMessageException("message_limits_exceeded");
        var body = Utf8.GetBytes(message.Payload);
        if (body.Length > maxBytes || Utf8.GetByteCount(RoutingKey(message)) >= 255)
            throw new PermanentMessageException("message_limits_exceeded");
        return (new BasicProperties
        {
            Persistent = true, ContentType = "application/json", MessageId = message.Id.ToString("N"),
            Type = RoutingKey(message), CorrelationId = message.CorrelationId,
            Headers = new Dictionary<string, object?>
            {
                ["ms-contract-name"] = message.ContractName,
                ["ms-contract-version"] = message.ContractVersion,
                ["ms-occurred-at"] = message.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["traceparent"] = message.TraceParent ?? "", ["tracestate"] = message.TraceState ?? ""
            }
        }, body);
    }

    public static SerializedMessage Decode(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body, int maxBytes)
    {
        if (body.Length > maxBytes || !Guid.TryParse(properties.MessageId, out var id) || id == Guid.Empty)
            throw new MessageContractException("Missing message identity or excessive payload size.");
        var name = Header(properties, "ms-contract-name");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200
            || !int.TryParse(Header(properties, "ms-contract-version"), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version <= 0 || !DateTimeOffset.TryParseExact(Header(properties, "ms-occurred-at"), "O",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred) || occurred.Offset != TimeSpan.Zero)
            throw new MessageContractException("Invalid message contract metadata.");
        try
        {
            var message = new SerializedMessage(id, name, version, occurred, Utf8.GetString(body.Span), properties.CorrelationId,
                Header(properties, "traceparent"), Header(properties, "tracestate"));
            MessageMetadataLimits.Validate(message.CorrelationId, message.TraceParent, message.TraceState);
            return message;
        }
        catch (DecoderFallbackException) { throw new MessageContractException("Message is not valid UTF-8."); }
    }

    private static string DecodeHeader(byte[] bytes)
    {
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new MessageContractException("Message header is not valid UTF-8."); }
    }

    private static string? Header(IReadOnlyBasicProperties properties, string name)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(name, out var value)) return null;
        return value switch
        {
            byte[] bytes => DecodeHeader(bytes),
            string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
