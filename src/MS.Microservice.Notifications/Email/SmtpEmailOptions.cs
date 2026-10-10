namespace MS.Microservice.Notifications.Email;

public enum SmtpTlsMode { RequireStartTls, TlsOnConnect, None }

public sealed class SmtpEmailOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 587;
    public SmtpTlsMode TlsMode { get; init; } = SmtpTlsMode.RequireStartTls;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public int TimeoutMilliseconds { get; init; } = 30_000;
    public int DisconnectTimeoutMilliseconds { get; init; } = 2_000;
}
