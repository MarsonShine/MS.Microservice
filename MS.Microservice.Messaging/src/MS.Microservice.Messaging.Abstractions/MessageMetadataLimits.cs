using System.Text;

namespace MS.Microservice.Messaging;

/// <summary>Limits shared by message producers, transports, and the self-managed Inbox/Outbox.</summary>
public static class MessageMetadataLimits
{
    public const int CorrelationIdMaxLength = 200;
    public const int CorrelationIdMaxUtf8Bytes = 255;
    public const int TraceParentMaxLength = 128;
    public const int TraceStateMaxLength = 512;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static bool AreValid(string? correlationId, string? traceParent, string? traceState)
    {
        if (correlationId?.Length > CorrelationIdMaxLength || traceParent?.Length > TraceParentMaxLength
            || traceState?.Length > TraceStateMaxLength) return false;
        try
        {
            // CorrelationId is an AMQP short string. Validate the other headers' UTF-8 round-trip too.
            if (correlationId is not null && Utf8.GetByteCount(correlationId) > CorrelationIdMaxUtf8Bytes)
                return false;
            if (traceParent is not null) Utf8.GetByteCount(traceParent);
            if (traceState is not null) Utf8.GetByteCount(traceState);
            return true;
        }
        catch (EncoderFallbackException) { return false; }
    }

    public static void Validate(string? correlationId, string? traceParent, string? traceState)
    {
        if (!AreValid(correlationId, traceParent, traceState))
            throw new MessageContractException("Invalid message metadata length or encoding.");
    }
}
