namespace MS.Microservice.Notifications.Sms;

public sealed record SmsMessage(string PhoneNumber, string SignName, string TemplateCode)
{
    public IReadOnlyDictionary<string, string>? TemplateParameters { get; init; }
    public string? OutId { get; init; }
}

/// <summary>The provider accepted submission; delivery must be confirmed by its delivery reports.</summary>
public sealed record SmsAcceptanceReceipt(string RequestId, string BizId, DateTimeOffset AcceptedAtUtc, string? OutId);

public sealed class SmsRejectedException(string code, string? requestId, string? providerMessage)
    : Exception("The SMS provider rejected the submission.")
{
    public string Code { get; } = code;
    public string? RequestId { get; } = requestId;
    public string? ProviderMessage { get; } = providerMessage;
}
