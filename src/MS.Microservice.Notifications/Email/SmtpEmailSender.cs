using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace MS.Microservice.Notifications.Email;

public sealed class SmtpEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly Func<SmtpClient> _createClient;

    public SmtpEmailSender(SmtpEmailOptions options) : this(options, static () => new SmtpClient()) { }

    internal SmtpEmailSender(SmtpEmailOptions options, Func<SmtpClient> createClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createClient);
        if (string.IsNullOrWhiteSpace(options.Host) || options.Host.Any(char.IsWhiteSpace)
            || options.Port is < 1 or > 65535 || !Enum.IsDefined(options.TlsMode)
            || options.TimeoutMilliseconds < 1 || options.DisconnectTimeoutMilliseconds < 1
            || (options.UserName is null) != (options.Password is null)
            || options.UserName is { Length: 0 } || options.Password is { Length: 0 }
            || options.UserName is not null && options.TlsMode == SmtpTlsMode.None)
            throw new ArgumentException("Configure an SMTP host/port, explicit TLS, positive timeouts and paired credentials over TLS.", nameof(options));
        _options = options;
        _createClient = createClient;
    }

    public async Task<SmtpAcceptanceReceipt> SendAsync(EmailMessage email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(email.To);
        ArgumentNullException.ThrowIfNull(email.Cc);
        ArgumentNullException.ThrowIfNull(email.Bcc);
        ValidateHeader(email.Subject, nameof(email.Subject));
        if (email.TextBody is null && email.HtmlBody is null)
            throw new ArgumentException("Provide a plain-text or HTML body.", nameof(email));
        using var message = new MimeMessage();
        var sender = Mailbox(email.From);
        message.From.Add(sender);
        foreach (var address in email.To) message.To.Add(Mailbox(address));
        foreach (var address in email.Cc) message.Cc.Add(Mailbox(address));
        foreach (var address in email.Bcc) message.Bcc.Add(Mailbox(address));
        var recipients = message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).ToArray();
        if (recipients.Length == 0) throw new ArgumentException("Provide at least one recipient.", nameof(email));
        message.Subject = email.Subject;
        message.MessageId = MimeUtils.GenerateMessageId();
        message.Date = DateTimeOffset.UtcNow;
        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();
        cancellationToken.ThrowIfCancellationRequested();
        var client = _createClient();
        client.Timeout = _options.TimeoutMilliseconds;
        SmtpAcceptanceReceipt receipt;
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, _options.TlsMode switch
            {
                SmtpTlsMode.RequireStartTls => SecureSocketOptions.StartTls,
                SmtpTlsMode.TlsOnConnect => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.None
            }, cancellationToken).ConfigureAwait(false);
            if (_options.UserName is not null)
                await client.AuthenticateAsync(_options.UserName, _options.Password!, cancellationToken).ConfigureAwait(false);
            var response = await client.SendAsync(message, sender, recipients, cancellationToken).ConfigureAwait(false);
            receipt = new(message.MessageId, response, DateTimeOffset.UtcNow);
        }
        catch
        {
            try { client.Dispose(); }
            catch { /* Keep the original error; the remote send outcome can be uncertain. */ }
            throw;
        }

        try
        {
            using var cleanup = new CancellationTokenSource(_options.DisconnectTimeoutMilliseconds);
            if (client.IsConnected) await client.DisconnectAsync(true, cleanup.Token).ConfigureAwait(false);
        }
        catch { /* Acceptance already succeeded; cleanup must not trigger a duplicate send. */ }
        finally
        {
            try { client.Dispose(); }
            catch { /* The accepted receipt remains authoritative. */ }
        }

        return receipt;
    }

    private static MailboxAddress Mailbox(EmailAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        ValidateHeader(address.Address, nameof(address.Address));
        if (address.DisplayName is not null) ValidateHeader(address.DisplayName, nameof(address.DisplayName));
        if (!MailboxAddress.TryParse(address.Address, out var parsed) || !string.IsNullOrEmpty(parsed.Name)
            || !string.Equals(parsed.Address, address.Address, StringComparison.Ordinal))
            throw new ArgumentException("Use one mailbox address; put its display name in DisplayName.", nameof(address));
        return new MailboxAddress(address.DisplayName ?? string.Empty, parsed.Address);
    }

    private static void ValidateHeader(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Email header values cannot contain line breaks or NUL.", parameter);
    }
}
