namespace MS.Microservice.Notifications.Email;

public sealed record EmailAddress(string Address, string? DisplayName = null);

public sealed record EmailMessage
{
    public required EmailAddress From { get; init; }
    public IReadOnlyList<EmailAddress> To { get; init; } = [];
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public required string Subject { get; init; }
    public string? TextBody { get; init; }
    public string? HtmlBody { get; init; }
}

/// <summary>The SMTP server accepted this message; this does not confirm delivery to recipients.</summary>
public sealed record SmtpAcceptanceReceipt(
    string MessageId, string ServerResponse, DateTimeOffset AcceptedAtUtc);
