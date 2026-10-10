using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using MS.Microservice.Notifications.Email;
using Xunit;

namespace MS.Microservice.Notifications.Tests.Email;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task Send_ServerAcceptancePreservesEnvelopeBccPrivacyAndAlternativeUnicodeBodies()
    {
        await using var server = new LocalSmtpServer();
        var sender = Sender(server.Port);

        var before = DateTimeOffset.UtcNow;
        var receipt = await sender.SendAsync(Message());
        await server.Completion;
        using var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(server.Data!)));

        Assert.Contains("accepted", receipt.ServerResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(message.MessageId, receipt.MessageId);
        Assert.InRange(receipt.AcceptedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Contains(server.Commands, line => line.StartsWith("MAIL FROM:<sender@example.test>", StringComparison.OrdinalIgnoreCase));
        foreach (var recipient in new[] { "to@example.test", "cc@example.test", "hidden@example.test" })
            Assert.Contains(server.Commands, line => line.StartsWith($"RCPT TO:<{recipient}>", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(message.Bcc);
        Assert.DoesNotContain("hidden@example.test", server.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("欢迎中文🙂", message.Subject);
        Assert.Equal("纯文本正文🙂", message.TextBody);
        Assert.Equal("<p>HTML 正文🙂</p>", message.HtmlBody);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_OnlyBccRecipientAndSingleBodyKindAreSupported(bool html)
    {
        await using var server = new LocalSmtpServer();
        var email = Message() with
        {
            To = [], Cc = [], TextBody = html ? null : "plain only", HtmlBody = html ? "<p>html only</p>" : null
        };

        var receipt = await Sender(server.Port).SendAsync(email);
        using var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(server.Data!)));

        Assert.NotEmpty(receipt.MessageId);
        Assert.Single(server.Commands, line => line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(server.Commands, line => line.StartsWith("RCPT TO:<hidden@example.test>", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(message.Bcc);
        Assert.Null(html ? message.TextBody : message.HtmlBody);
        Assert.Equal((html ? "<p>html only</p>" : "plain only") + "\r\n", html ? message.HtmlBody : message.TextBody);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_QuitFailureOrCallerCancellationAfterAcceptanceNeverMakesSendRetryable(bool quitFailure)
    {
        using var cancellation = new CancellationTokenSource();
        await using var server = new LocalSmtpServer
        {
            FailQuit = quitFailure,
            BeforeQuit = quitFailure ? null : cancellation.Cancel
        };

        var receipt = await Sender(server.Port).SendAsync(Message(), cancellation.Token);

        Assert.True(server.Accepted);
        Assert.NotEmpty(receipt.MessageId);
    }

    [Fact]
    public async Task Send_QuitTimeoutIsBoundedButKeepsAcceptedReceipt()
    {
        await using var server = new LocalSmtpServer { StallQuit = true };

        var receipt = await new SmtpEmailSender(new SmtpEmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, TlsMode = SmtpTlsMode.None,
            TimeoutMilliseconds = 5000, DisconnectTimeoutMilliseconds = 100
        }).SendAsync(Message());

        Assert.True(server.Accepted);
        Assert.NotEmpty(receipt.MessageId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_LocalCleanupExceptionsPreserveEitherAcceptanceOrPrimarySendFailure(bool rejectBeforeAcceptance)
    {
        await using var server = new LocalSmtpServer { RejectData = rejectBeforeAcceptance };
        var sender = new SmtpEmailSender(new SmtpEmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, TlsMode = SmtpTlsMode.None, TimeoutMilliseconds = 5000
        }, static () => new CleanupFailureClient());

        if (rejectBeforeAcceptance)
            await Assert.ThrowsAsync<SmtpCommandException>(() => sender.SendAsync(Message()));
        else
        {
            var receipt = await sender.SendAsync(Message());
            Assert.NotEmpty(receipt.MessageId);
        }
        Assert.Equal(!rejectBeforeAcceptance, server.Accepted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_RecipientOrDataRejectionPropagatesWithoutAutomaticRetry(bool rejectRecipient)
    {
        await using var server = new LocalSmtpServer { RejectRecipient = rejectRecipient, RejectData = !rejectRecipient };

        await Assert.ThrowsAsync<SmtpCommandException>(() => Sender(server.Port).SendAsync(Message()));

        Assert.False(server.Accepted);
        Assert.InRange(server.Commands.Count(line => line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase)), 0, 1);
    }

    [Fact]
    public async Task Send_RequiredStartTlsDoesNotDowngradeToUnencryptedSmtp()
    {
        await using var server = new LocalSmtpServer();
        var sender = new SmtpEmailSender(new SmtpEmailOptions { Host = "127.0.0.1", Port = server.Port });

        await Assert.ThrowsAsync<NotSupportedException>(() => sender.SendAsync(Message()));

        Assert.DoesNotContain(server.Commands, line => line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase));
        Assert.False(server.Accepted);
    }

    [Theory]
    [InlineData(SmtpTlsMode.RequireStartTls)]
    [InlineData(SmtpTlsMode.TlsOnConnect)]
    public async Task Send_RealTlsAndAuthenticationUseOnlyThePinnedLoopbackCertificate(SmtpTlsMode mode)
    {
        using var certificate = CreateCertificate();
        var pin = certificate.GetCertHash(HashAlgorithmName.SHA256);
        await using var server = new LocalSmtpServer
        {
            Certificate = certificate, ImplicitTls = mode == SmtpTlsMode.TlsOnConnect,
            AdvertiseStartTls = mode == SmtpTlsMode.RequireStartTls
        };
        var sender = new SmtpEmailSender(new SmtpEmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, TlsMode = mode, UserName = "test-user", Password = "test-password"
        }, () => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, presented, _, _) => presented is not null
                && CryptographicOperations.FixedTimeEquals(pin, presented.GetCertHash(HashAlgorithmName.SHA256))
        });

        SmtpAcceptanceReceipt receipt;
        try { receipt = await sender.SendAsync(Message()); }
        catch (Exception error)
        {
            await server.Completion;
            throw new AggregateException("Loopback TLS failed.", error, server.Failure ?? error);
        }

        Assert.True(server.UsedTls);
        Assert.True(server.Authenticated);
        Assert.True(server.Accepted);
        Assert.NotEmpty(receipt.MessageId);
    }

    [Fact]
    public async Task Send_PublicClientRejectsAnUntrustedLoopbackCertificate()
    {
        using var certificate = CreateCertificate();
        await using var server = new LocalSmtpServer { Certificate = certificate, ImplicitTls = true };
        var sender = new SmtpEmailSender(new SmtpEmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, TlsMode = SmtpTlsMode.TlsOnConnect, TimeoutMilliseconds = 5000
        });

        var failure = await Assert.ThrowsAsync<MailKit.Security.SslHandshakeException>(() => sender.SendAsync(Message()));

        Assert.NotNull(failure.ServerCertificate);
        Assert.Equal(certificate.GetCertHashString(), failure.ServerCertificate.GetCertHashString());
        Assert.False(server.Accepted);
        Assert.DoesNotContain(server.Commands, line => line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Send_CancellationDuringDataAcceptanceWaitDoesNotReportAcceptanceOrRetry()
    {
        using var cancellation = new CancellationTokenSource();
        await using var server = new LocalSmtpServer { BeforeAcceptance = cancellation.Cancel, StallAcceptance = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(server.Port).SendAsync(Message(), cancellation.Token));

        Assert.NotNull(server.Data);
        Assert.False(server.Accepted);
        Assert.Single(server.Commands, line => line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Send_PreCancellationDoesNotOpenAConnection()
    {
        await using var server = new LocalSmtpServer();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(server.Port).SendAsync(Message(), new(true)));

        Assert.False(server.Connected);
    }

    [Fact]
    public async Task Send_InvalidMailboxHeadersAndMissingRecipientsFailBeforeConnection()
    {
        await using var server = new LocalSmtpServer();
        var sender = Sender(server.Port);
        EmailMessage[] invalid =
        [
            Message() with { Subject = "subject\r\nBcc: injected@example.test" },
            Message() with { From = new("one@example.test, two@example.test") },
            Message() with { From = new("Name <sender@example.test>") },
            Message() with { To = [new("bad\r\nRCPT TO: injected@example.test")] },
            Message() with { To = [], Cc = [], Bcc = [] },
            Message() with { TextBody = null, HtmlBody = null }
        ];
        foreach (var message in invalid)
            await Assert.ThrowsAnyAsync<ArgumentException>(() => sender.SendAsync(message));
        Assert.False(server.Connected);
    }

    [Fact]
    public void Options_RequireExplicitValidConfigurationAndNeverExposePasswordThroughToString()
    {
        SmtpEmailOptions[] invalid =
        [
            new() { Host = "" }, new() { Host = "smtp\r\ninvalid" }, new() { Host = "localhost", Port = 0 },
            new() { Host = "localhost", TimeoutMilliseconds = 0 }, new() { Host = "localhost", DisconnectTimeoutMilliseconds = 0 },
            new() { Host = "localhost", UserName = "user" },
            new() { Host = "localhost", TlsMode = SmtpTlsMode.None, UserName = "user", Password = "secret" }
        ];
        foreach (var options in invalid) Assert.Throws<ArgumentException>(() => new SmtpEmailSender(options));
        var valid = new SmtpEmailOptions { Host = "smtp.example.test", UserName = "user", Password = "private-password" };
        Assert.DoesNotContain("private-password", valid.ToString()!, StringComparison.Ordinal);
    }

    private static SmtpEmailSender Sender(int port) => new(new SmtpEmailOptions
    {
        Host = "127.0.0.1", Port = port, TlsMode = SmtpTlsMode.None, TimeoutMilliseconds = 5000
    });

    private static EmailMessage Message() => new()
    {
        From = new("sender@example.test", "发送者"), To = [new("to@example.test")], Cc = [new("cc@example.test")],
        Bcc = [new("hidden@example.test")], Subject = "欢迎中文🙂", TextBody = "纯文本正文🙂", HtmlBody = "<p>HTML 正文🙂</p>"
    };

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Schannel requires a temporary key container, not CertificateRequest's ephemeral key handle.
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null,
            X509KeyStorageFlags.DefaultKeySet);
    }

    private sealed class CleanupFailureClient : SmtpClient
    {
        public override async Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            await base.DisconnectAsync(quit, cancellationToken);
            throw new IOException("injected local disconnect error");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) throw new IOException("injected local dispose error");
        }
    }

    private sealed class LocalSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        public LocalSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completion = Run();
        }

        public int Port { get; }
        public Task Completion { get; }
        public bool Connected { get; private set; }
        public Exception? Failure { get; private set; }
        public bool UsedTls { get; private set; }
        public bool Authenticated { get; private set; }
        public bool Accepted { get; private set; }
        public string? Data { get; private set; }
        public List<string> Commands { get; } = [];
        public bool FailQuit { get; init; }
        public bool StallQuit { get; init; }
        public bool RejectRecipient { get; init; }
        public bool RejectData { get; init; }
        public bool StallAcceptance { get; init; }
        public Action? BeforeAcceptance { get; init; }
        public Action? BeforeQuit { get; init; }
        public X509Certificate2? Certificate { get; init; }
        public bool ImplicitTls { get; init; }
        public bool AdvertiseStartTls { get; init; }

        private async Task Run()
        {
            try
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                Connected = true;
                await using var stream = connection.GetStream();
                if (ImplicitTls)
                {
                    using var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
                    await AuthenticateTls(ssl);
                    await Session(ssl, secure: true);
                }
                else await Session(stream, secure: false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or AuthenticationException)
            {
                Failure = error;
            }
        }

        private async Task AuthenticateTls(SslStream stream)
        {
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = Certificate!, ClientCertificateRequired = false
            }, _stopping.Token);
            UsedTls = true;
        }

        private async Task Session(Stream stream, bool secure, bool greeting = true)
        {
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                if (greeting) await writer.WriteLineAsync("220 localhost test SMTP");
                string? command;
                while ((command = await reader.ReadLineAsync(_stopping.Token)) is not null)
                {
                    Commands.Add(command);
                    if (command.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        if (AdvertiseStartTls && !secure) await writer.WriteLineAsync("250-STARTTLS");
                        if (secure) await writer.WriteLineAsync("250-AUTH PLAIN");
                        await writer.WriteLineAsync("250 8BITMIME");
                    }
                    else if (command.Equals("STARTTLS", StringComparison.OrdinalIgnoreCase) && AdvertiseStartTls && !secure)
                    {
                        await writer.WriteLineAsync("220 begin TLS");
                        using var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
                        await AuthenticateTls(ssl);
                        await Session(ssl, secure: true, greeting: false);
                        return;
                    }
                    else if (command.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase) && secure)
                    {
                        var encoded = command.Length > 11 ? command[11..] : string.Empty;
                        if (encoded.Length == 0)
                        {
                            await writer.WriteLineAsync("334 ");
                            encoded = await reader.ReadLineAsync(_stopping.Token) ?? "";
                        }
                        Authenticated = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)) == "\0test-user\0test-password";
                        await writer.WriteLineAsync(Authenticated ? "235 authenticated" : "535 authentication failed");
                    }
                    else if (command.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                        await writer.WriteLineAsync(RejectRecipient ? "550 recipient rejected" : "250 recipient OK");
                    else if (command.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 send message");
                        var data = new StringBuilder();
                        string? line;
                        while ((line = await reader.ReadLineAsync(_stopping.Token)) is not null && line != ".")
                            data.AppendLine(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
                        Data = data.ToString();
                        BeforeAcceptance?.Invoke();
                        if (StallAcceptance) { await Task.Delay(Timeout.Infinite, _stopping.Token); return; }
                        await writer.WriteLineAsync(RejectData ? "554 message rejected" : "250 accepted as local-test-queue");
                        Accepted = !RejectData;
                    }
                    else if (command.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        BeforeQuit?.Invoke();
                        if (StallQuit) { await Task.Delay(Timeout.Infinite, _stopping.Token); return; }
                        if (!FailQuit) await writer.WriteLineAsync("221 goodbye");
                        return;
                    }
                    else await writer.WriteLineAsync("250 OK");
                }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            await Completion;
            _stopping.Dispose();
        }
    }
}
